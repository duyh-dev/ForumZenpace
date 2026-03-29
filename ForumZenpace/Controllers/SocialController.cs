using System.Security.Claims;
using ForumZenpace.Hubs;
using ForumZenpace.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace ForumZenpace.Controllers
{
    [Authorize]
    public class SocialController : Controller
    {
        private readonly SocialService _socialService;
        private readonly IHubContext<SocialHub> _hubContext;

        public SocialController(SocialService socialService, IHubContext<SocialHub> hubContext)
        {
            _socialService = socialService;
            _hubContext = hubContext;
        }

        [HttpGet]
        public async Task<IActionResult> SearchFriendCandidates(string? term)
        {
            if (!TryGetCurrentUserId(out var userId))
            {
                return Challenge();
            }

            return Json(await _socialService.SearchFriendCandidatesAsync(userId, term, cancellationToken: HttpContext.RequestAborted));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendFriendRequest(int targetUserId, string? returnUrl = null)
        {
            if (!TryGetCurrentUserId(out var currentUserId))
            {
                return Challenge();
            }

            var result = await _socialService.SendFriendRequestAsync(currentUserId, targetUserId, HttpContext.RequestAborted);
            if (!result.Success)
            {
                return BuildFailureResult(result.ErrorMessage, returnUrl);
            }

            if (result.ReceiverNotification is not null)
            {
                await _hubContext.Clients.Group(SocialChannel.GetUserGroupName(targetUserId))
                    .SendAsync("NotificationUpserted", result.ReceiverNotification);
            }

            await _hubContext.Clients.Group(SocialChannel.GetUserGroupName(targetUserId))
                .SendAsync("NotificationCountChanged", new { unreadCount = result.ReceiverUnreadNotificationCount });

            await _hubContext.Clients.Group(SocialChannel.GetUserGroupName(currentUserId))
                .SendAsync("FriendRequestStateChanged", new
                {
                    userId = targetUserId,
                    state = "pending-sent"
                });

            await _hubContext.Clients.Group(SocialChannel.GetUserGroupName(targetUserId))
                .SendAsync("FriendRequestStateChanged", new
                {
                    userId = currentUserId,
                    state = "pending-received"
                });

            return BuildSuccessResult(returnUrl);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AcceptFriendRequest(int requestId, string? returnUrl = null)
        {
            if (!TryGetCurrentUserId(out var currentUserId))
            {
                return Challenge();
            }

            var result = await _socialService.AcceptFriendRequestAsync(currentUserId, requestId, HttpContext.RequestAborted);
            if (!result.Success)
            {
                return BuildFailureResult(result.ErrorMessage, returnUrl);
            }

            if (result.FriendForReceiver is not null)
            {
                await _hubContext.Clients.Group(SocialChannel.GetUserGroupName(currentUserId))
                    .SendAsync("FriendshipAdded", result.FriendForReceiver);
            }

            if (result.FriendForSender is not null)
            {
                await _hubContext.Clients.Group(SocialChannel.GetUserGroupName(result.SenderUserId))
                    .SendAsync("FriendshipAdded", result.FriendForSender);

                await _hubContext.Clients.Group(SocialChannel.GetUserGroupName(result.SenderUserId))
                    .SendAsync("FriendRequestStateChanged", new
                    {
                        userId = currentUserId,
                        state = "friend"
                    });
            }

            await _hubContext.Clients.Group(SocialChannel.GetUserGroupName(currentUserId))
                .SendAsync("FriendRequestResolved", new
                {
                    requestId,
                    status = Models.FriendRequestStatuses.Accepted,
                    unreadCount = result.ReceiverUnreadNotificationCount
                });

            await _hubContext.Clients.Group(SocialChannel.GetUserGroupName(currentUserId))
                .SendAsync("NotificationCountChanged", new { unreadCount = result.ReceiverUnreadNotificationCount });

            if (result.SenderNotification is not null && result.FriendForSender is not null)
            {
                await _hubContext.Clients.Group(SocialChannel.GetUserGroupName(result.SenderUserId))
                    .SendAsync("NotificationUpserted", result.SenderNotification);
            }

            if (result.FriendForSender is not null)
            {
                await _hubContext.Clients.Group(SocialChannel.GetUserGroupName(result.SenderUserId))
                    .SendAsync("NotificationCountChanged", new { unreadCount = result.SenderUnreadNotificationCount });
            }

            return BuildSuccessResult(returnUrl);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeclineFriendRequest(int requestId, string? returnUrl = null)
        {
            if (!TryGetCurrentUserId(out var currentUserId))
            {
                return Challenge();
            }

            var result = await _socialService.DeclineFriendRequestAsync(currentUserId, requestId, HttpContext.RequestAborted);
            if (!result.Success)
            {
                return BuildFailureResult(result.ErrorMessage, returnUrl);
            }

            await _hubContext.Clients.Group(SocialChannel.GetUserGroupName(currentUserId))
                .SendAsync("FriendRequestResolved", new
                {
                    requestId,
                    status = Models.FriendRequestStatuses.Declined,
                    unreadCount = result.ReceiverUnreadNotificationCount
                });

            await _hubContext.Clients.Group(SocialChannel.GetUserGroupName(currentUserId))
                .SendAsync("NotificationCountChanged", new { unreadCount = result.ReceiverUnreadNotificationCount });

            await _hubContext.Clients.Group(SocialChannel.GetUserGroupName(currentUserId))
                .SendAsync("FriendRequestStateChanged", new
                {
                    userId = result.SenderUserId,
                    state = "none"
                });

            await _hubContext.Clients.Group(SocialChannel.GetUserGroupName(result.SenderUserId))
                .SendAsync("FriendRequestStateChanged", new
                {
                    userId = currentUserId,
                    state = "none"
                });

            return BuildSuccessResult(returnUrl);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveFriend(int targetUserId, string? returnUrl = null)
        {
            if (!TryGetCurrentUserId(out var currentUserId))
            {
                return Challenge();
            }

            var result = await _socialService.RemoveFriendAsync(currentUserId, targetUserId, HttpContext.RequestAborted);
            if (!result.Success)
            {
                return BuildFailureResult(result.ErrorMessage, returnUrl);
            }

            await _hubContext.Clients.Group(SocialChannel.GetUserGroupName(currentUserId))
                .SendAsync("FriendshipRemoved", new { friendUserId = targetUserId });

            await _hubContext.Clients.Group(SocialChannel.GetUserGroupName(targetUserId))
                .SendAsync("FriendshipRemoved", new { friendUserId = currentUserId });

            await _hubContext.Clients.Group(SocialChannel.GetUserGroupName(currentUserId))
                .SendAsync("FriendRequestStateChanged", new
                {
                    userId = targetUserId,
                    state = "none"
                });

            await _hubContext.Clients.Group(SocialChannel.GetUserGroupName(targetUserId))
                .SendAsync("FriendRequestStateChanged", new
                {
                    userId = currentUserId,
                    state = "none"
                });

            return BuildSuccessResult(returnUrl);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleMessageBlock(int targetUserId, string? returnUrl = null)
        {
            if (!TryGetCurrentUserId(out var currentUserId))
            {
                return Challenge();
            }

            var result = await _socialService.ToggleMessageBlockAsync(currentUserId, targetUserId, HttpContext.RequestAborted);
            if (!result.Success)
            {
                return BuildFailureResult(result.ErrorMessage, returnUrl);
            }

            await _hubContext.Clients.Group(SocialChannel.GetUserGroupName(currentUserId))
                .SendAsync("MessageBlockChanged", new
                {
                    targetUserId,
                    isMessageBlockedByViewer = result.IsMessageBlockedByViewer,
                    isMessageBlockedByOtherUser = result.IsMessageBlockedByOtherUser,
                    isConversationBlocked = result.IsConversationBlocked
                });

            var reverseRelationship = await _socialService.GetRelationshipStatusAsync(targetUserId, currentUserId, HttpContext.RequestAborted);
            await _hubContext.Clients.Group(SocialChannel.GetUserGroupName(targetUserId))
                .SendAsync("MessageBlockChanged", new
                {
                    targetUserId = currentUserId,
                    isMessageBlockedByViewer = reverseRelationship.IsMessageBlockedByViewer,
                    isMessageBlockedByOtherUser = reverseRelationship.IsMessageBlockedByOtherUser,
                    isConversationBlocked = reverseRelationship.IsConversationBlocked
                });

            return BuildSuccessResult(returnUrl);
        }

        private IActionResult BuildSuccessResult(string? returnUrl)
        {
            if (IsAjaxRequest())
            {
                return Json(new { success = true });
            }

            if (Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl!);
            }

            return RedirectToAction("Index", "Home");
        }

        private IActionResult BuildFailureResult(string errorMessage, string? returnUrl)
        {
            if (IsAjaxRequest())
            {
                return BadRequest(new { success = false, message = errorMessage });
            }

            TempData["ErrorMessage"] = errorMessage;
            if (Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl!);
            }

            return RedirectToAction("Index", "Home");
        }

        private bool IsAjaxRequest()
        {
            return string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryGetCurrentUserId(out int userId)
        {
            return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
        }
    }
}
