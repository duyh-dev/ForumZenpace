using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using ForumZenpace.Hubs;
using ForumZenpace.Services;

namespace ForumZenpace.Controllers
{
    [Authorize]
    public class NotificationController : Controller
    {
        private readonly SocialService _socialService;
        private readonly IHubContext<SocialHub> _hubContext;

        public NotificationController(SocialService socialService, IHubContext<SocialHub> hubContext)
        {
            _socialService = socialService;
            _hubContext = hubContext;
        }

        public async Task<IActionResult> Index()
        {
            if (!TryGetCurrentUserId(out var userId))
            {
                return Challenge();
            }

            return View(await _socialService.GetNotificationPageAsync(userId));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAsRead(int id)
        {
            if (!TryGetCurrentUserId(out var userId))
            {
                return Challenge();
            }

            var unreadCount = await _socialService.MarkNotificationAsReadAsync(userId, id);
            await _hubContext.Clients.Group(SocialChannel.GetUserGroupName(userId))
                .SendAsync("NotificationCountChanged", new { unreadCount });
            return RedirectToAction(nameof(Index));
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllAsRead()
        {
            if (!TryGetCurrentUserId(out var userId))
            {
                return Challenge();
            }

            var unreadCount = await _socialService.MarkAllNotificationsAsReadAsync(userId);
            await _hubContext.Clients.Group(SocialChannel.GetUserGroupName(userId))
                .SendAsync("NotificationCountChanged", new { unreadCount });
            return RedirectToAction(nameof(Index));
        }

        private bool TryGetCurrentUserId(out int userId)
        {
            return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
        }
    }
}
