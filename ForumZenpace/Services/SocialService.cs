using ForumZenpace.Models;
using Microsoft.EntityFrameworkCore;

namespace ForumZenpace.Services
{
    public sealed class SocialService
    {
        private const int DefaultFriendSearchLimit = 8;
        private readonly ForumDbContext _context;

        public SocialService(ForumDbContext context)
        {
            _context = context;
        }

        public async Task<int> GetUnreadNotificationCountAsync(int userId, CancellationToken cancellationToken = default)
        {
            return await _context.Notifications
                .AsNoTracking()
                .CountAsync(notification => notification.UserId == userId && !notification.IsRead, cancellationToken);
        }

        public async Task<IReadOnlyList<FriendSummaryViewModel>> GetFriendsAsync(int userId, CancellationToken cancellationToken = default)
        {
            var friendships = await _context.Friendships
                .AsNoTracking()
                .Include(friendship => friendship.UserA)
                .Include(friendship => friendship.UserB)
                .Where(friendship => friendship.UserAId == userId || friendship.UserBId == userId)
                .OrderByDescending(friendship => friendship.CreatedAt)
                .ToListAsync(cancellationToken);

            if (friendships.Count == 0)
            {
                return Array.Empty<FriendSummaryViewModel>();
            }

            var friendUsers = friendships
                .Select(friendship => friendship.UserAId == userId ? friendship.UserB : friendship.UserA)
                .Where(friendUser => friendUser.IsActive)
                .ToList();

            var friendIds = friendUsers.Select(friendUser => friendUser.Id).ToList();
            var messageBlocks = await _context.MessageBlocks
                .AsNoTracking()
                .Where(block =>
                    (block.BlockerUserId == userId && friendIds.Contains(block.BlockedUserId)) ||
                    (block.BlockedUserId == userId && friendIds.Contains(block.BlockerUserId)))
                .ToListAsync(cancellationToken);

            return friendUsers
                .OrderBy(friendUser => GetDisplayName(friendUser.FullName, friendUser.Username))
                .Select(friendUser => BuildFriendSummary(userId, friendUser, messageBlocks))
                .ToList();
        }

        public async Task<IReadOnlyList<FriendCandidateViewModel>> SearchFriendCandidatesAsync(
            int currentUserId,
            string? term,
            int limit = DefaultFriendSearchLimit,
            CancellationToken cancellationToken = default)
        {
            var normalizedTerm = term?.Trim() ?? string.Empty;
            
            if (string.IsNullOrWhiteSpace(normalizedTerm))
            {
                return await GetFriendSuggestionsAsync(currentUserId, limit, cancellationToken);
            }

            var usersQuery = _context.Users
                .AsNoTracking()
                .Where(user => user.IsActive && user.Id != currentUserId);

            if (!string.IsNullOrWhiteSpace(normalizedTerm))
            {
                usersQuery = usersQuery.Where(user =>
                    user.Username.Contains(normalizedTerm) ||
                    user.FullName.Contains(normalizedTerm) ||
                    user.Email.Contains(normalizedTerm));
            }

            var users = await usersQuery
                .OrderBy(user => user.FullName)
                .ThenBy(user => user.Username)
                .Take(Math.Max(limit, 1) * 3)
                .ToListAsync(cancellationToken);

            if (users.Count == 0)
            {
                return Array.Empty<FriendCandidateViewModel>();
            }

            var candidateIds = users.Select(user => user.Id).ToList();
            var pendingFriendRequests = await _context.FriendRequests
                .AsNoTracking()
                .Where(friendRequest =>
                    friendRequest.Status == FriendRequestStatuses.Pending &&
                    ((friendRequest.SenderId == currentUserId && candidateIds.Contains(friendRequest.ReceiverId)) ||
                     (friendRequest.ReceiverId == currentUserId && candidateIds.Contains(friendRequest.SenderId))))
                .ToListAsync(cancellationToken);

            var friendships = await _context.Friendships
                .AsNoTracking()
                .Where(friendship =>
                    (friendship.UserAId == currentUserId && candidateIds.Contains(friendship.UserBId)) ||
                    (friendship.UserBId == currentUserId && candidateIds.Contains(friendship.UserAId)))
                .ToListAsync(cancellationToken);

            var blockedUserIds = await _context.MessageBlocks
                .AsNoTracking()
                .Where(block =>
                    (block.BlockerUserId == currentUserId && candidateIds.Contains(block.BlockedUserId)) ||
                    (block.BlockedUserId == currentUserId && candidateIds.Contains(block.BlockerUserId)))
                .Select(block => block.BlockerUserId == currentUserId ? block.BlockedUserId : block.BlockerUserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var friendIds = friendships
                .Select(friendship => friendship.UserAId == currentUserId ? friendship.UserBId : friendship.UserAId)
                .ToHashSet();

            var outgoingPendingIds = pendingFriendRequests
                .Where(friendRequest => friendRequest.SenderId == currentUserId)
                .Select(friendRequest => friendRequest.ReceiverId)
                .ToHashSet();

            var incomingPendingIds = pendingFriendRequests
                .Where(friendRequest => friendRequest.ReceiverId == currentUserId)
                .Select(friendRequest => friendRequest.SenderId)
                .ToHashSet();

            return users
                .Where(user => !blockedUserIds.Contains(user.Id))
                .Take(Math.Max(limit, 1))
                .Select(user =>
                {
                    var relationshipState = GetCandidateRelationshipState(user.Id, friendIds, outgoingPendingIds, incomingPendingIds);
                    return new FriendCandidateViewModel
                    {
                        UserId = user.Id,
                        Username = user.Username,
                        DisplayName = GetDisplayName(user.FullName, user.Username),
                        Email = EmailVerificationService.MaskEmail(user.Email),
                        AvatarUrl = user.Avatar,
                        RelationshipState = relationshipState,
                        CanSendRequest = relationshipState == "none",
                        ActionLabel = relationshipState switch
                        {
                            "friend" => "Xoa ban",
                            "pending-sent" => "Da gui loi moi",
                            "pending-received" => "Duyet",
                            _ => "Ket ban"
                        }
                    };
                })
                .ToList();
        }

        private async Task<IReadOnlyList<FriendCandidateViewModel>> GetFriendSuggestionsAsync(int currentUserId, int limit, CancellationToken cancellationToken)
        {
            var myFriendIds = await _context.Friendships
                .AsNoTracking()
                .Where(f => f.UserAId == currentUserId || f.UserBId == currentUserId)
                .Select(f => f.UserAId == currentUserId ? f.UserBId : f.UserAId)
                .ToListAsync(cancellationToken);

            if (myFriendIds.Count == 0)
            {
                var randomUsers = await _context.Users
                    .AsNoTracking()
                    .Where(u => u.IsActive && u.Id != currentUserId)
                    .OrderBy(u => Guid.NewGuid())
                    .Take(limit)
                    .ToListAsync(cancellationToken);
                
                return randomUsers.Select(user => new FriendCandidateViewModel
                {
                    UserId = user.Id,
                    Username = user.Username,
                    DisplayName = GetDisplayName(user.FullName, user.Username),
                    Email = EmailVerificationService.MaskEmail(user.Email),
                    AvatarUrl = user.Avatar,
                    RelationshipState = "none",
                    CanSendRequest = true,
                    ActionLabel = "Ket ban"
                }).ToList();
            }

            var pendingRequests = await _context.FriendRequests
                .AsNoTracking()
                .Where(r => r.Status == FriendRequestStatuses.Pending && (r.SenderId == currentUserId || r.ReceiverId == currentUserId))
                .ToListAsync(cancellationToken);
                
            var blocks = await _context.MessageBlocks
                .AsNoTracking()
                .Where(b => b.BlockerUserId == currentUserId || b.BlockedUserId == currentUserId)
                .ToListAsync(cancellationToken);

            var excludedIds = new HashSet<int>(myFriendIds);
            excludedIds.Add(currentUserId);
            foreach(var p in pendingRequests) excludedIds.Add(p.SenderId == currentUserId ? p.ReceiverId : p.SenderId);
            foreach(var b in blocks) excludedIds.Add(b.BlockerUserId == currentUserId ? b.BlockedUserId : b.BlockerUserId);

            var mutualCandidates = await _context.Friendships
                .AsNoTracking()
                .Where(f => myFriendIds.Contains(f.UserAId) || myFriendIds.Contains(f.UserBId))
                .Select(f => new {
                    CandidateId = myFriendIds.Contains(f.UserAId) ? f.UserBId : f.UserAId,
                    FriendInCommon = myFriendIds.Contains(f.UserAId) ? f.UserAId : f.UserBId
                })
                .ToListAsync(cancellationToken);

            var rankedCandidates = mutualCandidates
                .Where(x => !excludedIds.Contains(x.CandidateId))
                .GroupBy(x => x.CandidateId)
                .Select(g => new { UserId = g.Key, MutualCount = g.Select(x => x.FriendInCommon).Distinct().Count() })
                .OrderByDescending(x => x.MutualCount)
                .Take(limit)
                .ToList();

            if (rankedCandidates.Count == 0)
            {
                return new List<FriendCandidateViewModel>();
            }

            var candidateIds = rankedCandidates.Select(x => x.UserId).ToList();
            var users = await _context.Users
                .AsNoTracking()
                .Where(u => candidateIds.Contains(u.Id) && u.IsActive)
                .ToListAsync(cancellationToken);

            return rankedCandidates
                .Join(users, r => r.UserId, u => u.Id, (r, user) => new FriendCandidateViewModel
                {
                    UserId = user.Id,
                    Username = user.Username,
                    DisplayName = GetDisplayName(user.FullName, user.Username),
                    Email = EmailVerificationService.MaskEmail(user.Email),
                    AvatarUrl = user.Avatar,
                    RelationshipState = "none",
                    CanSendRequest = true,
                    ActionLabel = $"Ket ban ({r.MutualCount} chung)"
                })
                .ToList();
        }

        public async Task<RelationshipStatusViewModel> GetRelationshipStatusAsync(int currentUserId, int targetUserId, CancellationToken cancellationToken = default)
        {
            var (userAId, userBId) = OrderUsers(currentUserId, targetUserId);
            var friendshipExists = await _context.Friendships
                .AnyAsync(friendship => friendship.UserAId == userAId && friendship.UserBId == userBId, cancellationToken);

            var pendingRequest = await _context.FriendRequests
                .AsNoTracking()
                .Where(friendRequest =>
                    friendRequest.Status == FriendRequestStatuses.Pending &&
                    ((friendRequest.SenderId == currentUserId && friendRequest.ReceiverId == targetUserId) ||
                     (friendRequest.SenderId == targetUserId && friendRequest.ReceiverId == currentUserId)))
                .OrderByDescending(friendRequest => friendRequest.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var messageBlocks = await _context.MessageBlocks
                .AsNoTracking()
                .Where(block =>
                    (block.BlockerUserId == currentUserId && block.BlockedUserId == targetUserId) ||
                    (block.BlockerUserId == targetUserId && block.BlockedUserId == currentUserId))
                .ToListAsync(cancellationToken);

            var isBlockedByViewer = messageBlocks.Any(block => block.BlockerUserId == currentUserId);
            var isBlockedByOtherUser = messageBlocks.Any(block => block.BlockerUserId == targetUserId);

            return new RelationshipStatusViewModel
            {
                IsFriend = friendshipExists,
                HasIncomingFriendRequest = pendingRequest?.SenderId == targetUserId,
                HasOutgoingFriendRequest = pendingRequest?.SenderId == currentUserId,
                IncomingFriendRequestId = pendingRequest?.SenderId == targetUserId ? pendingRequest.Id : null,
                IsMessageBlockedByViewer = isBlockedByViewer,
                IsMessageBlockedByOtherUser = isBlockedByOtherUser,
                IsConversationBlocked = isBlockedByViewer || isBlockedByOtherUser
            };
        }

        public async Task<NotificationPageViewModel> GetNotificationPageAsync(int userId, CancellationToken cancellationToken = default)
        {
            var notifications = await _context.Notifications
                .AsNoTracking()
                .Include(notification => notification.ActorUser)
                .Include(notification => notification.FriendRequest)
                .Where(notification => notification.UserId == userId)
                .OrderByDescending(notification => notification.CreatedAt)
                .ToListAsync(cancellationToken);

            return new NotificationPageViewModel
            {
                CurrentUserId = userId,
                UnreadCount = notifications.Count(notification => !notification.IsRead),
                Items = notifications
                    .Select(notification => MapNotification(notification, userId))
                    .ToList()
            };
        }

        public async Task<int> MarkNotificationAsReadAsync(int userId, int notificationId, CancellationToken cancellationToken = default)
        {
            var notification = await _context.Notifications
                .FirstOrDefaultAsync(item => item.Id == notificationId && item.UserId == userId, cancellationToken);

            if (notification is not null && !notification.IsRead)
            {
                notification.IsRead = true;
                await _context.SaveChangesAsync(cancellationToken);
            }

            return await GetUnreadNotificationCountAsync(userId, cancellationToken);
        }

        public async Task<int> MarkAllNotificationsAsReadAsync(int userId, CancellationToken cancellationToken = default)
        {
            var unreadNotifications = await _context.Notifications
                .Where(notification => notification.UserId == userId && !notification.IsRead)
                .ToListAsync(cancellationToken);

            if (unreadNotifications.Count == 0)
            {
                return 0;
            }

            foreach (var notification in unreadNotifications)
            {
                notification.IsRead = true;
            }

            await _context.SaveChangesAsync(cancellationToken);
            return 0;
        }

        public async Task<SendFriendRequestResult> SendFriendRequestAsync(int senderId, int receiverId, CancellationToken cancellationToken = default)
        {
            if (senderId == receiverId)
            {
                return Failure<SendFriendRequestResult>("Ban khong the gui loi moi ket ban cho chinh minh.");
            }

            var sender = await _context.Users
                .FirstOrDefaultAsync(user => user.Id == senderId && user.IsActive, cancellationToken);
            var receiver = await _context.Users
                .FirstOrDefaultAsync(user => user.Id == receiverId && user.IsActive, cancellationToken);

            if (sender is null || receiver is null)
            {
                return Failure<SendFriendRequestResult>("Khong tim thay nguoi dung de ket ban.");
            }

            var relationship = await GetRelationshipStatusAsync(senderId, receiverId, cancellationToken);
            if (relationship.IsFriend)
            {
                return Failure<SendFriendRequestResult>("Hai ban da la ban be.");
            }

            if (relationship.IsConversationBlocked)
            {
                return Failure<SendFriendRequestResult>("Khong the gui loi moi khi mot trong hai ben da chan lien lac.");
            }

            if (relationship.HasOutgoingFriendRequest)
            {
                return Failure<SendFriendRequestResult>("Ban da gui loi moi ket ban toi nguoi nay.");
            }

            if (relationship.HasIncomingFriendRequest)
            {
                return Failure<SendFriendRequestResult>("Nguoi nay da gui loi moi ket ban cho ban. Hay vao thong bao de chap nhan.");
            }

            var friendRequest = new FriendRequest
            {
                SenderId = senderId,
                ReceiverId = receiverId,
                Status = FriendRequestStatuses.Pending,
                CreatedAt = DateTime.UtcNow
            };

            _context.FriendRequests.Add(friendRequest);
            await _context.SaveChangesAsync(cancellationToken);

            var notification = new Notification
            {
                UserId = receiverId,
                ActorUserId = senderId,
                FriendRequestId = friendRequest.Id,
                Type = NotificationTypes.FriendRequest,
                Content = $"{GetDisplayName(sender.FullName, sender.Username)} da gui loi moi ket ban cho ban.",
                CreatedAt = DateTime.UtcNow
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync(cancellationToken);

            return new SendFriendRequestResult
            {
                Success = true,
                TargetUserId = receiverId,
                ReceiverNotification = MapNotification(notification, receiverId, sender, friendRequest),
                ReceiverUnreadNotificationCount = await GetUnreadNotificationCountAsync(receiverId, cancellationToken)
            };
        }

        public async Task<AcceptFriendRequestResult> AcceptFriendRequestAsync(int receiverId, int requestId, CancellationToken cancellationToken = default)
        {
            var friendRequest = await _context.FriendRequests
                .Include(request => request.Sender)
                .Include(request => request.Receiver)
                .FirstOrDefaultAsync(request => request.Id == requestId, cancellationToken);

            if (friendRequest is null || friendRequest.ReceiverId != receiverId)
            {
                return Failure<AcceptFriendRequestResult>("Khong tim thay loi moi ket ban hop le.");
            }

            if (!string.Equals(friendRequest.Status, FriendRequestStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            {
                return Failure<AcceptFriendRequestResult>("Loi moi ket ban nay da duoc xu ly.");
            }

            var (userAId, userBId) = OrderUsers(friendRequest.SenderId, friendRequest.ReceiverId);
            var existingFriendship = await _context.Friendships
                .FirstOrDefaultAsync(friendship => friendship.UserAId == userAId && friendship.UserBId == userBId, cancellationToken);

            if (existingFriendship is null)
            {
                _context.Friendships.Add(new Friendship
                {
                    UserAId = userAId,
                    UserBId = userBId,
                    CreatedAt = DateTime.UtcNow
                });
            }

            friendRequest.Status = FriendRequestStatuses.Accepted;
            friendRequest.RespondedAt = DateTime.UtcNow;

            var receiverNotification = await _context.Notifications
                .Where(notification => notification.UserId == receiverId && notification.FriendRequestId == requestId)
                .ToListAsync(cancellationToken);

            foreach (var notification in receiverNotification)
            {
                notification.IsRead = true;
            }

            var senderAcceptedNotification = new Notification
            {
                UserId = friendRequest.SenderId,
                ActorUserId = receiverId,
                Type = NotificationTypes.FriendAccepted,
                Content = $"{GetDisplayName(friendRequest.Receiver.FullName, friendRequest.Receiver.Username)} da chap nhan loi moi ket ban cua ban.",
                CreatedAt = DateTime.UtcNow
            };

            _context.Notifications.Add(senderAcceptedNotification);
            await _context.SaveChangesAsync(cancellationToken);

            var senderBlocks = await GetPairMessageBlocksAsync(friendRequest.SenderId, friendRequest.ReceiverId, cancellationToken);

            return new AcceptFriendRequestResult
            {
                Success = true,
                RequestId = requestId,
                SenderUserId = friendRequest.SenderId,
                ReceiverUserId = receiverId,
                FriendForReceiver = BuildFriendSummary(receiverId, friendRequest.Sender, senderBlocks),
                FriendForSender = BuildFriendSummary(friendRequest.SenderId, friendRequest.Receiver, senderBlocks),
                SenderNotification = MapNotification(senderAcceptedNotification, friendRequest.SenderId, friendRequest.Receiver, null),
                SenderUnreadNotificationCount = await GetUnreadNotificationCountAsync(friendRequest.SenderId, cancellationToken),
                ReceiverUnreadNotificationCount = await GetUnreadNotificationCountAsync(receiverId, cancellationToken)
            };
        }

        public async Task<DeclineFriendRequestResult> DeclineFriendRequestAsync(int receiverId, int requestId, CancellationToken cancellationToken = default)
        {
            var friendRequest = await _context.FriendRequests
                .FirstOrDefaultAsync(request => request.Id == requestId && request.ReceiverId == receiverId, cancellationToken);

            if (friendRequest is null || !string.Equals(friendRequest.Status, FriendRequestStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            {
                return Failure<DeclineFriendRequestResult>("Khong tim thay loi moi ket ban de tu choi.");
            }

            friendRequest.Status = FriendRequestStatuses.Declined;
            friendRequest.RespondedAt = DateTime.UtcNow;

            var receiverNotifications = await _context.Notifications
                .Where(notification => notification.UserId == receiverId && notification.FriendRequestId == requestId)
                .ToListAsync(cancellationToken);

            foreach (var notification in receiverNotifications)
            {
                notification.IsRead = true;
            }

            await _context.SaveChangesAsync(cancellationToken);

            return new DeclineFriendRequestResult
            {
                Success = true,
                RequestId = requestId,
                SenderUserId = friendRequest.SenderId,
                ReceiverUnreadNotificationCount = await GetUnreadNotificationCountAsync(receiverId, cancellationToken)
            };
        }

        public async Task<RemoveFriendResult> RemoveFriendAsync(int currentUserId, int friendUserId, CancellationToken cancellationToken = default)
        {
            if (currentUserId == friendUserId)
            {
                return Failure<RemoveFriendResult>("Khong the xoa chinh minh khoi danh sach ban be.");
            }

            var (userAId, userBId) = OrderUsers(currentUserId, friendUserId);
            var friendship = await _context.Friendships
                .FirstOrDefaultAsync(item => item.UserAId == userAId && item.UserBId == userBId, cancellationToken);

            if (friendship is null)
            {
                return Failure<RemoveFriendResult>("Hai ban hien khong o trong danh sach ban be.");
            }

            _context.Friendships.Remove(friendship);
            await _context.SaveChangesAsync(cancellationToken);

            return new RemoveFriendResult
            {
                Success = true,
                FriendUserId = friendUserId
            };
        }

        public async Task<ToggleMessageBlockResult> ToggleMessageBlockAsync(int currentUserId, int targetUserId, CancellationToken cancellationToken = default)
        {
            if (currentUserId == targetUserId)
            {
                return Failure<ToggleMessageBlockResult>("Khong the chan tin nhan cua chinh minh.");
            }

            var targetUserExists = await _context.Users
                .AsNoTracking()
                .AnyAsync(user => user.Id == targetUserId && user.IsActive, cancellationToken);

            if (!targetUserExists)
            {
                return Failure<ToggleMessageBlockResult>("Khong tim thay nguoi dung can cap nhat quyen nhan tin.");
            }

            var existingBlock = await _context.MessageBlocks
                .FirstOrDefaultAsync(block => block.BlockerUserId == currentUserId && block.BlockedUserId == targetUserId, cancellationToken);

            if (existingBlock is null)
            {
                _context.MessageBlocks.Add(new MessageBlock
                {
                    BlockerUserId = currentUserId,
                    BlockedUserId = targetUserId,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                _context.MessageBlocks.Remove(existingBlock);
            }

            await _context.SaveChangesAsync(cancellationToken);

            var relationship = await GetRelationshipStatusAsync(currentUserId, targetUserId, cancellationToken);
            return new ToggleMessageBlockResult
            {
                Success = true,
                TargetUserId = targetUserId,
                IsMessageBlockedByViewer = relationship.IsMessageBlockedByViewer,
                IsMessageBlockedByOtherUser = relationship.IsMessageBlockedByOtherUser,
                IsConversationBlocked = relationship.IsConversationBlocked
            };
        }

        private async Task<List<MessageBlock>> GetPairMessageBlocksAsync(int firstUserId, int secondUserId, CancellationToken cancellationToken)
        {
            return await _context.MessageBlocks
                .AsNoTracking()
                .Where(block =>
                    (block.BlockerUserId == firstUserId && block.BlockedUserId == secondUserId) ||
                    (block.BlockerUserId == secondUserId && block.BlockedUserId == firstUserId))
                .ToListAsync(cancellationToken);
        }

        private NotificationItemViewModel MapNotification(
            Notification notification,
            int currentUserId,
            User? actorUserOverride = null,
            FriendRequest? friendRequestOverride = null)
        {
            var actorUser = actorUserOverride ?? notification.ActorUser;
            var friendRequest = friendRequestOverride ?? notification.FriendRequest;

            return new NotificationItemViewModel
            {
                Id = notification.Id,
                Type = notification.Type,
                Content = notification.Content,
                IsRead = notification.IsRead,
                CreatedAt = notification.CreatedAt,
                ActorUserId = actorUser?.Id ?? notification.ActorUserId,
                ActorUsername = actorUser?.Username ?? string.Empty,
                ActorDisplayName = actorUser is null ? string.Empty : GetDisplayName(actorUser.FullName, actorUser.Username),
                ActorAvatarUrl = actorUser?.Avatar,
                FriendRequestId = friendRequest?.Id ?? notification.FriendRequestId,
                StoryId = notification.StoryId,
                TargetUrl = notification.StoryId.HasValue ? StoryService.GetStoryViewerUrl(notification.StoryId.Value) : string.Empty,
                ActionLabel = notification.StoryId.HasValue ? "Mo story" : string.Empty,
                FriendRequestStatus = friendRequest?.Status ?? string.Empty,
                CanAcceptFriendRequest =
                    string.Equals(notification.Type, NotificationTypes.FriendRequest, StringComparison.OrdinalIgnoreCase) &&
                    friendRequest?.ReceiverId == currentUserId &&
                    string.Equals(friendRequest.Status, FriendRequestStatuses.Pending, StringComparison.OrdinalIgnoreCase),
                CanDeclineFriendRequest =
                    string.Equals(notification.Type, NotificationTypes.FriendRequest, StringComparison.OrdinalIgnoreCase) &&
                    friendRequest?.ReceiverId == currentUserId &&
                    string.Equals(friendRequest.Status, FriendRequestStatuses.Pending, StringComparison.OrdinalIgnoreCase)
            };
        }

        private FriendSummaryViewModel BuildFriendSummary(int currentUserId, User friendUser, IReadOnlyCollection<MessageBlock> messageBlocks)
        {
            return new FriendSummaryViewModel
            {
                UserId = friendUser.Id,
                Username = friendUser.Username,
                DisplayName = GetDisplayName(friendUser.FullName, friendUser.Username),
                AvatarUrl = friendUser.Avatar,
                IsMessageBlockedByViewer = messageBlocks.Any(block => block.BlockerUserId == currentUserId && block.BlockedUserId == friendUser.Id),
                IsMessageBlockedByOtherUser = messageBlocks.Any(block => block.BlockerUserId == friendUser.Id && block.BlockedUserId == currentUserId)
            };
        }

        private static string GetCandidateRelationshipState(
            int candidateUserId,
            IReadOnlySet<int> friendIds,
            IReadOnlySet<int> outgoingPendingIds,
            IReadOnlySet<int> incomingPendingIds)
        {
            if (friendIds.Contains(candidateUserId))
            {
                return "friend";
            }

            if (outgoingPendingIds.Contains(candidateUserId))
            {
                return "pending-sent";
            }

            if (incomingPendingIds.Contains(candidateUserId))
            {
                return "pending-received";
            }

            return "none";
        }

        private static string GetDisplayName(string? fullName, string username)
        {
            return string.IsNullOrWhiteSpace(fullName) ? username : fullName.Trim();
        }

        private static (int UserAId, int UserBId) OrderUsers(int firstUserId, int secondUserId)
        {
            return firstUserId < secondUserId
                ? (firstUserId, secondUserId)
                : (secondUserId, firstUserId);
        }

        private static T Failure<T>(string errorMessage) where T : class, new()
        {
            var result = new T();
            switch (result)
            {
                case SendFriendRequestResult sendFriendRequestResult:
                    sendFriendRequestResult.Success = false;
                    sendFriendRequestResult.ErrorMessage = errorMessage;
                    return sendFriendRequestResult as T ?? result;
                case AcceptFriendRequestResult acceptFriendRequestResult:
                    acceptFriendRequestResult.Success = false;
                    acceptFriendRequestResult.ErrorMessage = errorMessage;
                    return acceptFriendRequestResult as T ?? result;
                case DeclineFriendRequestResult declineFriendRequestResult:
                    declineFriendRequestResult.Success = false;
                    declineFriendRequestResult.ErrorMessage = errorMessage;
                    return declineFriendRequestResult as T ?? result;
                case RemoveFriendResult removeFriendResult:
                    removeFriendResult.Success = false;
                    removeFriendResult.ErrorMessage = errorMessage;
                    return removeFriendResult as T ?? result;
                case ToggleMessageBlockResult toggleMessageBlockResult:
                    toggleMessageBlockResult.Success = false;
                    toggleMessageBlockResult.ErrorMessage = errorMessage;
                    return toggleMessageBlockResult as T ?? result;
                default:
                    return result;
            }
        }
    }

    public static class SocialChannel
    {
        public static string GetUserGroupName(int userId)
        {
            return $"user:{userId}";
        }
    }
}
