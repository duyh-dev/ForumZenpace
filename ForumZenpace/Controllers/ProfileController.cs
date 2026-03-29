using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ForumZenpace.Hubs;
using System.Security.Claims;
using ForumZenpace.Formatting;
using ForumZenpace.Models;
using ForumZenpace.Services;

namespace ForumZenpace.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private static readonly HashSet<string> AllowedAvatarExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".gif",
            ".webp"
        };
        private const long MaxAvatarSizeBytes = 5 * 1024 * 1024;
        private readonly ForumDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly DirectMessageService _directMessageService;
        private readonly SocialService _socialService;
        private readonly StoryService _storyService;
        private readonly EmailVerificationService _emailVerificationService;
        private readonly AuthFlowTokenService _authFlowTokenService;
        private readonly IHubContext<DirectMessageHub> _hubContext;

        public ProfileController(
            ForumDbContext context,
            IWebHostEnvironment environment,
            DirectMessageService directMessageService,
            SocialService socialService,
            StoryService storyService,
            EmailVerificationService emailVerificationService,
            AuthFlowTokenService authFlowTokenService,
            IHubContext<DirectMessageHub> hubContext)
        {
            _context = context;
            _environment = environment;
            _directMessageService = directMessageService;
            _socialService = socialService;
            _storyService = storyService;
            _emailVerificationService = emailVerificationService;
            _authFlowTokenService = authFlowTokenService;
            _hubContext = hubContext;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? tab = null)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return Challenge();

            var user = await _context.Users.FindAsync(userId.Value);
            
            if (user == null) return NotFound();

            ApplyFlashMessages();
            return View(await BuildProfileViewModelAsync(user, userId.Value, tab ?? "posts"));
        }

        [AllowAnonymous]
        [HttpGet("Profile/user/{username}")]
        public async Task<IActionResult> UserProfile(string username, string? tab = null)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return NotFound();
            }

            var profileUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == username.Trim() && u.IsActive);

            if (profileUser == null)
            {
                return NotFound();
            }

            var viewerUserId = GetCurrentUserId();
            if (viewerUserId.HasValue && viewerUserId.Value == profileUser.Id)
            {
                return RedirectToAction(nameof(Index));
            }

            ApplyFlashMessages();
            return View("Index", await BuildProfileViewModelAsync(profileUser, viewerUserId, tab ?? "posts"));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(ProfileViewModel model)
        {
            var userId = GetCurrentUserId();
            if (userId is null) return Challenge();

            var user = await _context.Users.FindAsync(userId.Value);

            if (user == null) return NotFound();

            model.FullName = model.FullName?.Trim() ?? string.Empty;
            model.Email = model.Email?.Trim() ?? string.Empty;

            ValidateAvatarFile(model.AvatarFile);

            var emailChanged = !string.Equals(user.Email, model.Email, StringComparison.OrdinalIgnoreCase);
            if (emailChanged && await _context.Users.AnyAsync(u => u.Id != user.Id && u.Email == model.Email))
            {
                ModelState.AddModelError(nameof(ProfileViewModel.Email), "Email da duoc su dung cho mot tai khoan khac.");
            }

            if (!ModelState.IsValid)
            {
                return View("Index", await BuildProfileViewModelAsync(user, userId.Value, "posts", model));
            }

            user.FullName = model.FullName;

            if (emailChanged)
            {
                user.Email = model.Email;
                user.IsEmailConfirmed = false;
                user.EmailVerificationToken = null;
                user.EmailVerificationTokenExpiresAt = null;
                user.PasswordResetToken = null;
                user.PasswordResetTokenExpiresAt = null;
            }

            if (model.AvatarFile is not null)
            {
                user.Avatar = await SaveAvatarAsync(model.AvatarFile, user.Id);
            }

            await _context.SaveChangesAsync();

            if (emailChanged)
            {
                var emailSent = await TrySendOtpAsync(user);
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                TempData[emailSent ? "AuthSuccessMessage" : "AuthErrorMessage"] = emailSent
                    ? "Email moi cua ban can duoc xac thuc. Chung toi da gui ma OTP moi va da dang xuat ban de bao ve tai khoan."
                    : "Email moi chua duoc xac thuc va he thong chua gui duoc OTP. Ban da duoc dang xuat; vui long kiem tra EmailJsSettings.";

                return RedirectToAction("VerifyEmail", "Auth", new
                {
                    token = _authFlowTokenService.CreateEmailVerificationToken(user.Id, EmailVerificationService.GetFlowTokenLifetime())
                });
            }

            ViewBag.SuccessMessage = "Cap nhat ho so thanh cong.";
            return View("Index", await BuildProfileViewModelAsync(user, userId.Value, "posts"));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendMessage(SendDirectMessageViewModel model)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
            {
                if (IsAjaxRequest())
                {
                    return Unauthorized(new { success = false, message = "Ban can dang nhap de gui tin nhan." });
                }

                return Challenge();
            }

            var username = model.Username?.Trim() ?? string.Empty;
            var result = await _directMessageService.SendMessageAsync(userId.Value, model);
            if (!result.Success || result.Message is null)
            {
                if (IsAjaxRequest())
                {
                    return Json(new { success = false, message = result.ErrorMessage });
                }

                TempData["ErrorMessage"] = result.ErrorMessage;
                return RedirectToAction(nameof(UserProfile), new { username, tab = "chat" });
            }

            await _hubContext.Clients.Group(result.ConversationGroupName)
                .SendAsync("DirectMessageReceived", result.Message);

            if (IsAjaxRequest())
            {
                return Json(new
                {
                    success = true,
                    message = result.Message
                });
            }

            TempData["SuccessMessage"] = $"Da gui tin nhan cho {result.TargetDisplayName}.";
            return RedirectToAction(nameof(UserProfile), new { username = result.TargetUsername, tab = "chat" });
        }

        private void ValidateAvatarFile(IFormFile? avatarFile)
        {
            if (avatarFile is null)
            {
                return;
            }

            if (avatarFile.Length == 0)
            {
                ModelState.AddModelError(nameof(ProfileViewModel.AvatarFile), "Anh tai len dang trong.");
                return;
            }

            if (avatarFile.Length > MaxAvatarSizeBytes)
            {
                ModelState.AddModelError(nameof(ProfileViewModel.AvatarFile), "Anh dai dien chi duoc toi da 5MB.");
            }

            var extension = Path.GetExtension(avatarFile.FileName);
            if (string.IsNullOrWhiteSpace(extension) || !AllowedAvatarExtensions.Contains(extension))
            {
                ModelState.AddModelError(nameof(ProfileViewModel.AvatarFile), "Chi chap nhan file JPG, PNG, GIF hoac WEBP.");
            }
        }

        private async Task<string> SaveAvatarAsync(IFormFile avatarFile, int userId)
        {
            var webRootPath = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
            var avatarDirectory = Path.Combine(webRootPath, "uploads", "avatars");
            Directory.CreateDirectory(avatarDirectory);

            var extension = Path.GetExtension(avatarFile.FileName).ToLowerInvariant();
            var fileName = $"avatar-{userId}-{Guid.NewGuid():N}{extension}";
            var filePath = Path.Combine(avatarDirectory, fileName);

            await using var stream = new FileStream(filePath, FileMode.Create);
            await avatarFile.CopyToAsync(stream);

            return $"/uploads/avatars/{fileName}";
        }

        private int? GetCurrentUserId()
        {
            return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                ? userId
                : null;
        }

        private async Task<ProfileViewModel> BuildProfileViewModelAsync(User user, int? viewerUserId, string activeTab, ProfileViewModel? source = null)
        {
            var isOwner = viewerUserId.HasValue && viewerUserId.Value == user.Id;
            var showChatTab = !isOwner;
            var relationshipStatus = new RelationshipStatusViewModel();
            if (showChatTab && viewerUserId.HasValue)
            {
                relationshipStatus = await _socialService.GetRelationshipStatusAsync(viewerUserId.Value, user.Id);
            }

            var showStoriesTab = isOwner || relationshipStatus.IsFriend;
            var normalizedActiveTab = NormalizeProfileTab(activeTab, showStoriesTab, showChatTab);

            var posts = await _context.Posts
                .Where(p => p.UserId == user.Id && p.Status == "Active")
                .Include(p => p.Category)
                .Include(p => p.Comments)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            IReadOnlyList<ProfileChatMessageViewModel> chatMessages = Array.Empty<ProfileChatMessageViewModel>();
            var chatMessageCount = 0;
            if (showChatTab && viewerUserId.HasValue)
            {
                var conversation = await GetConversationQuery(viewerUserId.Value, user.Id)
                    .Select(conversation => new { conversation.Id })
                    .FirstOrDefaultAsync();

                if (conversation != null)
                {
                    chatMessageCount = await _context.DirectMessages
                        .CountAsync(message => message.ConversationId == conversation.Id);

                    if (normalizedActiveTab == "chat")
                    {
                        var unreadMessages = await _context.DirectMessages
                            .Where(message =>
                                message.ConversationId == conversation.Id &&
                                message.SenderId != viewerUserId.Value &&
                                !message.IsRead)
                            .ToListAsync();

                        if (unreadMessages.Count > 0)
                        {
                            foreach (var message in unreadMessages)
                            {
                                message.IsRead = true;
                            }

                            await _context.SaveChangesAsync();
                        }

                        var messages = await _context.DirectMessages
                            .Where(message => message.ConversationId == conversation.Id)
                            .Include(message => message.Sender)
                            .Include(message => message.ReplyToMessage)
                                .ThenInclude(reply => reply!.Sender)
                            .OrderBy(message => message.CreatedAt)
                            .ToListAsync();

                        chatMessages = messages
                            .Select(message => new ProfileChatMessageViewModel
                            {
                                Id = message.Id,
                                Content = message.Content,
                                CreatedAt = message.CreatedAt,
                                IsOwnMessage = message.SenderId == viewerUserId.Value,
                                SenderDisplayName = DirectMessageService.GetDisplayName(message.Sender.Username, message.Sender.FullName),
                                ReplyTo = DirectMessageService.MapReplyPreview(message.ReplyToMessage)
                            })
                            .ToList();
                    }
                }
            }

            var stories = await _storyService.GetProfileStoriesAsync(user.Id, viewerUserId, showStoriesTab, HttpContext.RequestAborted);
            var activeStoryCount = stories.Count(story => !story.IsExpired);
            var archivedStoryCount = stories.Count - activeStoryCount;

            return new ProfileViewModel
            {
                FullName = source?.FullName ?? user.FullName,
                Email = source?.Email ?? user.Email,
                Avatar = user.Avatar,
                Username = user.Username,
                ProfileUserId = user.Id,
                ViewerUserId = viewerUserId,
                IsOwner = isOwner,
                IsAuthenticatedViewer = viewerUserId.HasValue,
                ActiveTab = normalizedActiveTab,
                ShowStoriesTab = showStoriesTab,
                ShowChatTab = showChatTab,
                CanSendMessages = viewerUserId.HasValue && !isOwner && !relationshipStatus.IsConversationBlocked,
                IsFriend = relationshipStatus.IsFriend,
                HasIncomingFriendRequest = relationshipStatus.HasIncomingFriendRequest,
                HasOutgoingFriendRequest = relationshipStatus.HasOutgoingFriendRequest,
                IncomingFriendRequestId = relationshipStatus.IncomingFriendRequestId,
                IsMessageBlockedByViewer = relationshipStatus.IsMessageBlockedByViewer,
                IsMessageBlockedByOtherUser = relationshipStatus.IsMessageBlockedByOtherUser,
                IsConversationBlocked = relationshipStatus.IsConversationBlocked,
                ChatAvailabilityMessage = relationshipStatus.IsMessageBlockedByViewer
                    ? "Ban da chan tin nhan voi nguoi dung nay."
                    : relationshipStatus.IsMessageBlockedByOtherUser
                        ? "Nguoi dung nay da chan tin nhan voi ban."
                        : string.Empty,
                ChatMessageCount = chatMessageCount,
                ChatMessages = chatMessages,
                JoinedAt = user.CreatedAt,
                PostCount = posts.Count,
                StoryCount = stories.Count,
                ActiveStoryCount = activeStoryCount,
                ArchivedStoryCount = archivedStoryCount,
                TotalViewCount = posts.Sum(p => p.ViewCount),
                TotalCommentCount = posts.Sum(p => p.Comments.Count),
                Posts = posts.Select(p => new ProfilePostSummaryViewModel
                {
                    Id = p.Id,
                    Title = p.Title,
                    Excerpt = PostContentFormatter.ToExcerpt(p.Content, 148),
                    CategoryName = p.Category?.Name ?? string.Empty,
                    CreatedAt = p.CreatedAt,
                    CommentCount = p.Comments.Count,
                    ViewCount = p.ViewCount
                }).ToList(),
                Stories = stories
            };
        }

        private IQueryable<DirectConversation> GetConversationQuery(int userId, int targetUserId)
        {
            var (userAId, userBId) = OrderConversationUsers(userId, targetUserId);
            return _context.DirectConversations.Where(conversation => conversation.UserAId == userAId && conversation.UserBId == userBId);
        }

        private static (int UserAId, int UserBId) OrderConversationUsers(int firstUserId, int secondUserId)
        {
            return firstUserId < secondUserId
                ? (firstUserId, secondUserId)
                : (secondUserId, firstUserId);
        }

        private static string NormalizeProfileTab(string? tab, bool allowStories = false, bool allowChat = false)
        {
            if (allowStories && string.Equals(tab, "stories", StringComparison.OrdinalIgnoreCase))
            {
                return "stories";
            }

            if (allowChat && string.Equals(tab, "chat", StringComparison.OrdinalIgnoreCase))
            {
                return "chat";
            }

            return "posts";
        }

        private void ApplyFlashMessages()
        {
            if (TempData["SuccessMessage"] is string successMessage)
            {
                ViewBag.SuccessMessage = successMessage;
            }

            if (TempData["ErrorMessage"] is string errorMessage)
            {
                ViewBag.ErrorMessage = errorMessage;
            }
        }

        private bool IsAjaxRequest()
        {
            return string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<bool> TrySendOtpAsync(User user)
        {
            try
            {
                if (!_emailVerificationService.CanIssueOtp(user, out _))
                {
                    return false;
                }

                var otpCode = _emailVerificationService.IssueOtp(user);
                await _context.SaveChangesAsync();
                await _emailVerificationService.SendOtpEmailAsync(user, otpCode, HttpContext.RequestAborted);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
