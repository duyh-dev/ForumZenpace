using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.RegularExpressions;
using ForumZenpace.Models;
using ForumZenpace.Services;

namespace ForumZenpace.Controllers
{
    [Authorize]
    public class PostController : Controller
    {
        private readonly ForumDbContext _context;
        private readonly RecommendationService _recommendationService;
        private readonly PostImageService _postImageService;
        private readonly IBackgroundTaskQueue _taskQueue;
        private readonly GeminiEmbeddingService _geminiService;

        public PostController(
            ForumDbContext context, 
            RecommendationService recommendationService,
            PostImageService postImageService,
            IBackgroundTaskQueue taskQueue,
            GeminiEmbeddingService geminiService)
        {
            _context = context;
            _recommendationService = recommendationService;
            _postImageService = postImageService;
            _taskQueue = taskQueue;
            _geminiService = geminiService;
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            ViewBag.Categories = new SelectList(await _context.Categories.ToListAsync(), "Id", "Name");
            return View(new PostViewModel
            {
                DraftToken = _postImageService.CreateDraftToken()
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PostViewModel model)
        {
            model.DraftToken = _postImageService.SanitizeDraftToken(model.DraftToken);
            if (string.IsNullOrWhiteSpace(model.DraftToken))
            {
                model.DraftToken = _postImageService.CreateDraftToken();
            }

            if (!ModelState.IsValid)
            {
                ViewBag.Categories = new SelectList(await _context.Categories.ToListAsync(), "Id", "Name", model.CategoryId);
                return View(model);
            }

            if (!TryGetCurrentUserId(out var userId))
            {
                return Challenge();
            }

            var post = new Post
            {
                Title = model.Title,
                Content = model.Content,
                CategoryId = model.CategoryId,
                UserId = userId
            };

            _context.Posts.Add(post);
            await _context.SaveChangesAsync();
            await _postImageService.AttachDraftImagesToPostAsync(post.Id, userId, model.DraftToken, model.Content);

            // Generate embedding vector for recommendation engine asynchronously
            var postId = post.Id;
            await _taskQueue.QueueBackgroundWorkItemAsync(async (sp, token) =>
            {
                var scopedContext = sp.GetRequiredService<ForumDbContext>();
                var scopedRecommendation = sp.GetRequiredService<RecommendationService>();
                var dbPost = await scopedContext.Posts.FindAsync(new object[] { postId }, token);
                if (dbPost != null) {
                    await scopedRecommendation.GeneratePostEmbeddingAsync(dbPost);
                }
            });

            return RedirectToAction("Details", new { id = post.Id });
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            if (!TryGetCurrentUserId(out var userId))
            {
                return Challenge();
            }

            var post = await _context.Posts.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
            
            if (post == null) return NotFound("Post not found or you don't have permission.");

            ViewBag.Categories = new SelectList(await _context.Categories.ToListAsync(), "Id", "Name", post.CategoryId);
            ViewBag.PostId = id;
            
            return View(new PostViewModel {
                PostId = post.Id,
                DraftToken = _postImageService.CreateDraftToken(),
                Title = post.Title,
                Content = post.Content,
                CategoryId = post.CategoryId
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, PostViewModel model)
        {
            if (!TryGetCurrentUserId(out var userId))
            {
                return Challenge();
            }

            var post = await _context.Posts.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

            if (post == null) return NotFound();

            model.PostId = id;
            model.DraftToken = _postImageService.SanitizeDraftToken(model.DraftToken);
            if (string.IsNullOrWhiteSpace(model.DraftToken))
            {
                model.DraftToken = _postImageService.CreateDraftToken();
            }

            if (!ModelState.IsValid)
            {
                ViewBag.Categories = new SelectList(await _context.Categories.ToListAsync(), "Id", "Name", model.CategoryId);
                ViewBag.PostId = id;
                return View(model);
            }

            post.Title = model.Title;
            post.Content = model.Content;
            post.CategoryId = model.CategoryId;
            post.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _postImageService.AttachDraftImagesToPostAsync(post.Id, userId, model.DraftToken, model.Content);
            await _postImageService.SyncPostImagesAsync(post.Id, userId, model.Content);

            return RedirectToAction("Details", new { id = post.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadImage(PostImageUploadViewModel model)
        {
            if (!TryGetCurrentUserId(out var userId))
            {
                return Challenge();
            }

            var validationError = _postImageService.ValidatePostImage(model.Image);
            if (validationError != null)
            {
                return Json(new { success = false, message = validationError });
            }

            model.DraftToken = _postImageService.SanitizeDraftToken(model.DraftToken);

            if (model.PostId.HasValue)
            {
                var canEditPost = await _context.Posts.AnyAsync(p => p.Id == model.PostId.Value && p.UserId == userId);
                if (!canEditPost)
                {
                    return Json(new { success = false, message = "Ban khong co quyen chen anh vao bai viet nay." });
                }
            }
            else if (string.IsNullOrWhiteSpace(model.DraftToken))
            {
                return Json(new { success = false, message = "Khong tim thay phien soan bai hop le." });
            }

            var saveAsDraftImage = !string.IsNullOrWhiteSpace(model.DraftToken);

            var (fileName, imageUrl) = await _postImageService.SavePostImageAsync(model.Image, userId);
            var originalFileName = Path.GetFileName(model.Image.FileName);

            var postImage = new PostImage
            {
                UserId = userId,
                PostId = saveAsDraftImage ? null : model.PostId,
                DraftToken = saveAsDraftImage ? model.DraftToken : null,
                FileName = fileName,
                OriginalFileName = originalFileName,
                ContentType = model.Image.ContentType ?? "application/octet-stream",
                ImageUrl = imageUrl
            };

            _context.PostImages.Add(postImage);
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                imageUrl,
                markdown = _postImageService.CreateImageMarkdown(postImage)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            if (!TryGetCurrentUserId(out var userId))
            {
                return Challenge();
            }

            var post = await _context.Posts.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

            if (post != null)
            {
                var commentIds = await _context.Comments
                    .Where(c => c.PostId == id)
                    .Select(c => c.Id)
                    .ToListAsync();
                var comments = await _context.Comments.Where(c => c.PostId == id).ToListAsync();
                var commentLikes = await _context.CommentLikes.Where(cl => commentIds.Contains(cl.CommentId)).ToListAsync();
                var likes = await _context.Likes.Where(l => l.PostId == id).ToListAsync();
                var postImages = await _context.PostImages.Where(pi => pi.PostId == id).ToListAsync();
                var reports = await _context.Reports.Where(r => r.PostId == id).ToListAsync();

                _context.CommentLikes.RemoveRange(commentLikes);
                _context.Comments.RemoveRange(comments);
                _context.Likes.RemoveRange(likes);
                _context.PostImages.RemoveRange(postImages);
                _context.Reports.RemoveRange(reports);
                _postImageService.DeletePostImageFiles(postImages);

                _context.Posts.Remove(post);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Index", "Home");
        }

        [AllowAnonymous]
        public async Task<IActionResult> Details(int id)
        {
            var post = await _context.Posts
                .Include(p => p.User)
                .Include(p => p.Category)
                .Include(p => p.Likes)
                .Include(p => p.Comments)
                    .ThenInclude(c => c.User)
                .Include(p => p.Comments)
                    .ThenInclude(c => c.CommentLikes)
                .FirstOrDefaultAsync(p => p.Id == id && p.Status == "Active");

            if (post == null) return NotFound();

            // Increase view count
            post.ViewCount++;
            await _context.SaveChangesAsync();
            
            var userId = GetCurrentUserId();
            if (userId.HasValue)
            {
                ViewBag.HasLiked = post.Likes.Any(l => l.UserId == userId.Value);
            }

            ViewBag.UserId = userId;
            
            var commentThreads = BuildCommentThreads(post.Comments, post.Id, userId, userId.HasValue);
            ViewBag.CommentThreads = commentThreads;

            return View(post);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddComment(CommentViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Content)) 
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Json(new { success = false, message = "Comment content cannot be empty" });
                return RedirectToAction("Details", new { id = model.PostId });
            }

            if (!TryGetCurrentUserId(out var userId))
            {
                return Challenge();
            }

            var comment = new Comment
            {
                Content = model.Content,
                PostId = model.PostId,
                ParentId = model.ParentId,
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Comments.Add(comment);
            
            // Notify Post Owner
            var post = await _context.Posts.FindAsync(model.PostId);
            if (post != null && post.UserId != userId)
            {
                var receiverId = post.UserId;
                var title = post.Title;
                await _taskQueue.QueueBackgroundWorkItemAsync(async (sp, token) =>
                {
                    var scopedContext = sp.GetRequiredService<ForumDbContext>();
                    scopedContext.Notifications.Add(new Notification
                    {
                        UserId = receiverId,
                        Content = $"Someone commented on your post '{title}'."
                    });
                    await scopedContext.SaveChangesAsync(token);
                });
            }

            await _context.SaveChangesAsync();

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                _context.Entry(comment).Reference(c => c.User).Load();
                return Json(new { 
                    success = true, 
                    id = comment.Id,
                    content = comment.Content,
                    author = comment.User.FullName,
                    date = comment.CreatedAt.ToString("MMM dd HH:mm"),
                    parentId = comment.ParentId
                });
            }

            return RedirectToAction("Details", new { id = model.PostId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleLike(int postId)
        {
            if (!TryGetCurrentUserId(out var userId))
            {
                return Challenge();
            }
            
            var existingLike = await _context.Likes.FirstOrDefaultAsync(l => l.UserId == userId && l.PostId == postId);
            bool isLiked = false;

            if (existingLike != null)
            {
                _context.Likes.Remove(existingLike);
            }
            else
            {
                _context.Likes.Add(new Like { UserId = userId, PostId = postId });
                isLiked = true;
                
                var post = await _context.Posts.FindAsync(postId);
                if (post != null && post.UserId != userId)
                {
                    var receiverId = post.UserId;
                    var title = post.Title;
                    await _taskQueue.QueueBackgroundWorkItemAsync(async (sp, token) =>
                    {
                        var scopedContext = sp.GetRequiredService<ForumDbContext>();
                        scopedContext.Notifications.Add(new Notification
                        {
                            UserId = receiverId,
                            Content = $"Someone liked your post '{title}'."
                        });
                        await scopedContext.SaveChangesAsync(token);
                    });
                }
            }

            await _context.SaveChangesAsync();

            // Safely update user preference vector on background thread
            await _taskQueue.QueueBackgroundWorkItemAsync(async (sp, token) =>
            {
                var scopedRecommendationService = sp.GetRequiredService<RecommendationService>();
                await scopedRecommendationService.UpdateUserPreferenceVectorAsync(userId);
            });

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var likeCount = await _context.Likes.CountAsync(l => l.PostId == postId);
                return Json(new { success = true, liked = isLiked, likeCount = likeCount });
            }

            return RedirectToAction("Details", new { id = postId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleCommentLike(int commentId)
        {
            if (!TryGetCurrentUserId(out var userId))
            {
                return Challenge();
            }

            var comment = await _context.Comments
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == commentId);

            if (comment == null)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Json(new { success = false, message = "Comment not found." });

                return RedirectToAction("Index", "Home");
            }

            var existingLike = await _context.CommentLikes
                .FirstOrDefaultAsync(cl => cl.UserId == userId && cl.CommentId == commentId);
            bool isLiked = false;

            if (existingLike != null)
            {
                _context.CommentLikes.Remove(existingLike);
            }
            else
            {
                _context.CommentLikes.Add(new CommentLike { UserId = userId, CommentId = commentId });
                isLiked = true;

                if (comment.UserId != userId)
                {
                    var receiverId = comment.UserId;
                    await _taskQueue.QueueBackgroundWorkItemAsync(async (sp, token) =>
                    {
                        var scopedContext = sp.GetRequiredService<ForumDbContext>();
                        scopedContext.Notifications.Add(new Notification
                        {
                            UserId = receiverId,
                            Content = "Someone liked your comment."
                        });
                        await scopedContext.SaveChangesAsync(token);
                    });
                }
            }

            await _context.SaveChangesAsync();

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var likeCount = await _context.CommentLikes.CountAsync(cl => cl.CommentId == commentId);
                return Json(new { success = true, liked = isLiked, likeCount = likeCount, commentId = commentId });
            }

            return RedirectToAction("Details", new { id = comment.PostId });
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Report(int postId, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) 
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Json(new { success = false, message = "Reason is required." });
                return RedirectToAction("Details", new { id = postId });
            }
            
            if (!TryGetCurrentUserId(out var userId))
            {
                return Challenge();
            }

            _context.Reports.Add(new Report
            {
                PostId = postId,
                UserId = userId,
                Reason = reason
            });
            await _context.SaveChangesAsync();
            
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { success = true, message = "Report submitted successfully." });
                
            return RedirectToAction("Details", new { id = postId, reportSuccess = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteComment(int id)
        {
            if (!TryGetCurrentUserId(out var userId))
            {
                return Challenge();
            }

            var comment = await _context.Comments.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
            
            if (comment != null)
            {
                var postId = comment.PostId;
                var commentBranch = await GetCommentBranchAsync(comment);
                var commentIds = commentBranch
                    .Select(c => c.Id)
                    .Append(comment.Id)
                    .ToList();
                var commentLikes = await _context.CommentLikes
                    .Where(cl => commentIds.Contains(cl.CommentId))
                    .ToListAsync();

                _context.CommentLikes.RemoveRange(commentLikes);
                _context.Comments.RemoveRange(commentBranch);
                _context.Comments.Remove(comment);
                await _context.SaveChangesAsync();
                
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Json(new { success = true });
                    
                return RedirectToAction("Details", new { id = postId });
            }

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { success = false, message = "Comment not found or unauthorized." });

            return RedirectToAction("Index", "Home");
        }

        private static List<CommentThreadViewModel> BuildCommentThreads(IEnumerable<Comment> comments, int postId, int? currentUserId, bool isAuthenticated)
        {
            var orderedComments = comments
                .OrderBy(c => c.CreatedAt)
                .ToList();

            var commentLookup = orderedComments.ToDictionary(c => c.Id);
            var childrenLookup = orderedComments
                .Where(c => c.ParentId.HasValue && commentLookup.ContainsKey(c.ParentId.Value))
                .GroupBy(c => c.ParentId!.Value)
                .ToDictionary(group => group.Key, group => group.OrderBy(c => c.CreatedAt).ToList());

            var rootComments = orderedComments
                .Where(c => !c.ParentId.HasValue || !commentLookup.ContainsKey(c.ParentId.Value))
                .ToList();

            var threads = new List<CommentThreadViewModel>(rootComments.Count);
            foreach (var rootComment in rootComments)
            {
                var replies = new List<CommentReplyViewModel>();
                CollectFlattenedReplies(rootComment.Id, rootComment.Id, 1, childrenLookup, commentLookup, replies);

                threads.Add(new CommentThreadViewModel
                {
                    RootComment = rootComment,
                    Replies = replies
                        .OrderBy(reply => reply.Comment.CreatedAt)
                        .ToList(),
                    PostId = postId,
                    CurrentUserId = currentUserId,
                    IsAuthenticated = isAuthenticated
                });
            }

            return threads;
        }

        private static void CollectFlattenedReplies(
            int parentCommentId,
            int rootCommentId,
            int depth,
            IReadOnlyDictionary<int, List<Comment>> childrenLookup,
            IReadOnlyDictionary<int, Comment> commentLookup,
            ICollection<CommentReplyViewModel> replies)
        {
            if (!childrenLookup.TryGetValue(parentCommentId, out var children))
            {
                return;
            }

            foreach (var child in children)
            {
                string? replyingToAuthorName = null;
                if (child.ParentId.HasValue
                    && commentLookup.TryGetValue(child.ParentId.Value, out var parentComment)
                    && parentComment.Id != rootCommentId)
                {
                    replyingToAuthorName = GetCommentAuthorName(parentComment);
                }

                replies.Add(new CommentReplyViewModel
                {
                    Comment = child,
                    Depth = depth,
                    ReplyingToAuthorName = replyingToAuthorName
                });

                CollectFlattenedReplies(child.Id, rootCommentId, depth + 1, childrenLookup, commentLookup, replies);
            }
        }

        private static string GetCommentAuthorName(Comment comment)
        {
            return string.IsNullOrWhiteSpace(comment.User?.FullName)
                ? "Unknown user"
                : comment.User.FullName;
        }

        private async Task<List<Comment>> GetCommentBranchAsync(Comment rootComment)
        {
            var postComments = await _context.Comments
                .Where(c => c.PostId == rootComment.PostId)
                .ToListAsync();

            var childrenLookup = postComments
                .Where(c => c.ParentId.HasValue)
                .ToLookup(c => c.ParentId!.Value);

            var idsToDelete = new HashSet<int>();
            var pendingCommentIds = new Queue<int>();
            pendingCommentIds.Enqueue(rootComment.Id);

            while (pendingCommentIds.Count > 0)
            {
                var currentId = pendingCommentIds.Dequeue();
                foreach (var childComment in childrenLookup[currentId])
                {
                    if (idsToDelete.Add(childComment.Id))
                    {
                        pendingCommentIds.Enqueue(childComment.Id);
                    }
                }
            }

            return postComments
                .Where(c => idsToDelete.Contains(c.Id))
                .ToList();
        }

        private int? GetCurrentUserId()
        {
            return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                ? userId
                : null;
        }

        private bool TryGetCurrentUserId(out int userId)
        {
            return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
        }

        [HttpGet]
        public async Task<IActionResult> Suggestions(int id)
        {
            if (!TryGetCurrentUserId(out var userId)) return Challenge();

            var post = await _context.Posts.FindAsync(id);
            if (post == null) return NotFound(new { success = false, message = "Post not found." });

            var suggestions = await _geminiService.GenerateCommentSuggestionsAsync(userId, post.Id, post.Title, post.Content, HttpContext.RequestAborted);
            return Json(new { success = true, suggestions });
        }
    }
}
