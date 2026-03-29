using System.Security.Claims;
using ForumZenpace.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ForumZenpace.Hubs
{
    [Authorize]
    public sealed class SocialHub : Hub
    {
        private readonly SocialService _socialService;
        private readonly PresenceTracker _presenceTracker;

        public SocialHub(SocialService socialService, PresenceTracker presenceTracker)
        {
            _socialService = socialService;
            _presenceTracker = presenceTracker;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = GetCurrentUserId();
            await Groups.AddToGroupAsync(Context.ConnectionId, SocialChannel.GetUserGroupName(userId));

            var isFirstConnection = await _presenceTracker.UserConnectedAsync(userId, Context.ConnectionId);
            if (isFirstConnection)
            {
                // Broadcast to all other clients that this user came online
                await Clients.Others.SendAsync("UserOnline", new { userId });
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = GetCurrentUserId();
            var isLastConnection = await _presenceTracker.UserDisconnectedAsync(userId, Context.ConnectionId);
            if (isLastConnection)
            {
                // Broadcast to all other clients that this user went offline
                await Clients.Others.SendAsync("UserOffline", new { userId });
            }

            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>Client sends this periodically to prove it's still alive.</summary>
        public async Task Heartbeat()
        {
            await _presenceTracker.HeartbeatAsync(GetCurrentUserId());
        }

        /// <summary>Client calls this on first connect to get current online user list.</summary>
        public async Task<int[]> GetOnlineUsers()
        {
            return await _presenceTracker.GetOnlineUsersAsync();
        }

        public async Task<IReadOnlyList<Models.FriendCandidateViewModel>> SearchFriendCandidates(string? term)
        {
            return await _socialService.SearchFriendCandidatesAsync(GetCurrentUserId(), term);
        }

        public async Task SendFriendRequest(int targetUserId)
        {
            var currentUserId = GetCurrentUserId();
            var result = await _socialService.SendFriendRequestAsync(currentUserId, targetUserId);
            if (!result.Success)
            {
                throw new HubException(result.ErrorMessage);
            }

            if (result.ReceiverNotification is not null)
            {
                await Clients.Group(SocialChannel.GetUserGroupName(targetUserId))
                    .SendAsync("NotificationUpserted", result.ReceiverNotification);
            }

            await Clients.Group(SocialChannel.GetUserGroupName(targetUserId))
                .SendAsync("NotificationCountChanged", new { unreadCount = result.ReceiverUnreadNotificationCount });

            await Clients.Group(SocialChannel.GetUserGroupName(currentUserId))
                .SendAsync("FriendRequestStateChanged", new
                {
                    userId = targetUserId,
                    state = "pending-sent"
                });

            await Clients.Group(SocialChannel.GetUserGroupName(targetUserId))
                .SendAsync("FriendRequestStateChanged", new
                {
                    userId = currentUserId,
                    state = "pending-received"
                });
        }

        public async Task AcceptFriendRequest(int requestId)
        {
            var currentUserId = GetCurrentUserId();
            var result = await _socialService.AcceptFriendRequestAsync(currentUserId, requestId);
            if (!result.Success)
            {
                throw new HubException(result.ErrorMessage);
            }

            if (result.FriendForReceiver is not null)
            {
                await Clients.Group(SocialChannel.GetUserGroupName(currentUserId))
                    .SendAsync("FriendshipAdded", result.FriendForReceiver);
            }

            if (result.FriendForSender is not null)
            {
                await Clients.Group(SocialChannel.GetUserGroupName(result.SenderUserId))
                    .SendAsync("FriendshipAdded", result.FriendForSender);

                await Clients.Group(SocialChannel.GetUserGroupName(result.SenderUserId))
                    .SendAsync("FriendRequestStateChanged", new
                    {
                        userId = currentUserId,
                        state = "friend"
                    });
            }

            await Clients.Group(SocialChannel.GetUserGroupName(currentUserId))
                .SendAsync("FriendRequestResolved", new
                {
                    requestId,
                    status = Models.FriendRequestStatuses.Accepted,
                    unreadCount = result.ReceiverUnreadNotificationCount
                });

            await Clients.Group(SocialChannel.GetUserGroupName(currentUserId))
                .SendAsync("NotificationCountChanged", new { unreadCount = result.ReceiverUnreadNotificationCount });

            if (result.SenderNotification is not null && result.FriendForSender is not null)
            {
                await Clients.Group(SocialChannel.GetUserGroupName(result.SenderUserId))
                    .SendAsync("NotificationUpserted", result.SenderNotification);
            }

            if (result.FriendForSender is not null)
            {
                await Clients.Group(SocialChannel.GetUserGroupName(result.SenderUserId))
                    .SendAsync("NotificationCountChanged", new { unreadCount = result.SenderUnreadNotificationCount });
            }
        }

        public async Task DeclineFriendRequest(int requestId)
        {
            var currentUserId = GetCurrentUserId();
            var result = await _socialService.DeclineFriendRequestAsync(currentUserId, requestId);
            if (!result.Success)
            {
                throw new HubException(result.ErrorMessage);
            }

            await Clients.Group(SocialChannel.GetUserGroupName(currentUserId))
                .SendAsync("FriendRequestResolved", new
                {
                    requestId,
                    status = Models.FriendRequestStatuses.Declined,
                    unreadCount = result.ReceiverUnreadNotificationCount
                });

            await Clients.Group(SocialChannel.GetUserGroupName(currentUserId))
                .SendAsync("NotificationCountChanged", new { unreadCount = result.ReceiverUnreadNotificationCount });

            await Clients.Group(SocialChannel.GetUserGroupName(currentUserId))
                .SendAsync("FriendRequestStateChanged", new
                {
                    userId = result.SenderUserId,
                    state = "none"
                });

            await Clients.Group(SocialChannel.GetUserGroupName(result.SenderUserId))
                .SendAsync("FriendRequestStateChanged", new
                {
                    userId = currentUserId,
                    state = "none"
                });
        }

        public async Task RemoveFriend(int friendUserId)
        {
            var currentUserId = GetCurrentUserId();
            var result = await _socialService.RemoveFriendAsync(currentUserId, friendUserId);
            if (!result.Success)
            {
                throw new HubException(result.ErrorMessage);
            }

            await Clients.Group(SocialChannel.GetUserGroupName(currentUserId))
                .SendAsync("FriendshipRemoved", new { friendUserId });

            await Clients.Group(SocialChannel.GetUserGroupName(friendUserId))
                .SendAsync("FriendshipRemoved", new { friendUserId = currentUserId });

            await Clients.Group(SocialChannel.GetUserGroupName(currentUserId))
                .SendAsync("FriendRequestStateChanged", new
                {
                    userId = friendUserId,
                    state = "none"
                });

            await Clients.Group(SocialChannel.GetUserGroupName(friendUserId))
                .SendAsync("FriendRequestStateChanged", new
                {
                    userId = currentUserId,
                    state = "none"
                });
        }

        public async Task ToggleMessageBlock(int targetUserId)
        {
            var currentUserId = GetCurrentUserId();
            var result = await _socialService.ToggleMessageBlockAsync(currentUserId, targetUserId);
            if (!result.Success)
            {
                throw new HubException(result.ErrorMessage);
            }

            await Clients.Group(SocialChannel.GetUserGroupName(currentUserId))
                .SendAsync("MessageBlockChanged", new
                {
                    targetUserId,
                    isMessageBlockedByViewer = result.IsMessageBlockedByViewer,
                    isMessageBlockedByOtherUser = result.IsMessageBlockedByOtherUser,
                    isConversationBlocked = result.IsConversationBlocked
                });

            var reverseRelationship = await _socialService.GetRelationshipStatusAsync(targetUserId, currentUserId);
            await Clients.Group(SocialChannel.GetUserGroupName(targetUserId))
                .SendAsync("MessageBlockChanged", new
                {
                    targetUserId = currentUserId,
                    isMessageBlockedByViewer = reverseRelationship.IsMessageBlockedByViewer,
                    isMessageBlockedByOtherUser = reverseRelationship.IsMessageBlockedByOtherUser,
                    isConversationBlocked = reverseRelationship.IsConversationBlocked
                });
        }

        private int GetCurrentUserId()
        {
            if (!int.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                throw new HubException("Ban can dang nhap de su dung tinh nang xa hoi.");
            }

            return userId;
        }
    }
}
