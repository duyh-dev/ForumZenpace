using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ForumZenpace.Models;
using System.Security.Claims;

namespace ForumZenpace.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ForumDbContext _context;

        public AdminController(ForumDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Dashboard()
        {
            var stats = new
            {
                TotalPosts = await _context.Posts.CountAsync(),
                TotalUsers = await _context.Users.CountAsync(),
                TopLiked = await _context.Posts.Include(p => p.User).OrderByDescending(p => p.Likes.Count).FirstOrDefaultAsync(),
                TopViewed = await _context.Posts.Include(p => p.User).OrderByDescending(p => p.ViewCount).FirstOrDefaultAsync(),
                PendingReports = await _context.Reports.CountAsync()
            };

            ViewBag.Stats = stats;
            return View();
        }

        public async Task<IActionResult> Users()
        {
            var users = await _context.Users.Include(u => u.Role).ToListAsync();
            return View(users);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleUserStatus(int id)
        {
            if (!TryGetCurrentUserId(out var currentUserId))
            {
                return Challenge();
            }

            var user = await _context.Users.FindAsync(id);
            if (user != null && user.Id != currentUserId) // Don't block yourself
            {
                user.IsActive = !user.IsActive;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Users));
        }

        public async Task<IActionResult> Posts()
        {
            var posts = await _context.Posts.Include(p => p.User).Include(p => p.Category).ToListAsync();
            return View(posts);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePostStatus(int id)
        {
            var post = await _context.Posts.FindAsync(id);
            if (post != null)
            {
                post.Status = post.Status == "Active" ? "Hidden" : "Active";
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Posts));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePost(int id)
        {
            var post = await _context.Posts.FindAsync(id);
            if (post != null)
            {
                var commentIds = await _context.Comments
                    .Where(c => c.PostId == id)
                    .Select(c => c.Id)
                    .ToListAsync();
                var comments = await _context.Comments.Where(c => c.PostId == id).ToListAsync();
                var commentLikes = await _context.CommentLikes.Where(cl => commentIds.Contains(cl.CommentId)).ToListAsync();
                var likes = await _context.Likes.Where(l => l.PostId == id).ToListAsync();
                var reports = await _context.Reports.Where(r => r.PostId == id).ToListAsync();

                _context.CommentLikes.RemoveRange(commentLikes);
                _context.Comments.RemoveRange(comments);
                _context.Likes.RemoveRange(likes);
                _context.Reports.RemoveRange(reports);

                _context.Posts.Remove(post);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Posts));
        }

        public async Task<IActionResult> Categories()
        {
            return View(await _context.Categories.ToListAsync());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddCategory(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                _context.Categories.Add(new Category { Name = name });
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Categories));
        }

        public async Task<IActionResult> Reports()
        {
            var reports = await _context.Reports
                .Include(r => r.User)
                .Include(r => r.Post)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
            return View(reports);
        }

        private bool TryGetCurrentUserId(out int userId)
        {
            return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
        }
    }
}
