using ForumZenpace.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace ForumZenpace.Services
{
    public sealed class DirectMessageService
    {
        private const int MaxMessageLength = 1000;
        private const int ReplyPreviewMaxLength = 120;
        private readonly ForumDbContext _context;

        public DirectMessageService(ForumDbContext context)
        {
            _context = context;
        }

        public async Task<DirectMessageSendResult> SendMessageAsync(int senderUserId, SendDirectMessageViewModel model, CancellationToken cancellationToken = default)
        {
            var username = model.Username?.Trim() ?? string.Empty;
            var content = model.Content?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(content))
            {
                return Failure("Noi dung tin nhan khong duoc de trong.");
            }

            if (content.Length > MaxMessageLength)
            {
                return Failure("Tin nhan chi duoc toi da 1000 ky tu.");
            }

            var sender = await _context.Users
                .Where(user => user.Id == senderUserId && user.IsActive)
                .Select(user => new
                {
                    user.Id,
                    user.Username,
                    user.FullName
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (sender is null)
            {
                return Failure("Ban can dang nhap de gui tin nhan.");
            }

            var targetUser = await _context.Users
                .Where(user => user.Id == model.TargetUserId && user.Username == username && user.IsActive)
                .Select(user => new
                {
                    user.Id,
                    user.Username,
                    user.FullName
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (targetUser is null)
            {
                return Failure("Khong tim thay nguoi dung nhan tin.");
            }

            if (targetUser.Id == senderUserId)
            {
                return Failure("Ban khong the tu nhan tin cho chinh minh.");
            }

            if (model.IsStoryReply && model.StoryId.HasValue)
            {
                var story = await _context.Stories.FindAsync(new object[] { model.StoryId.Value }, cancellationToken);
                if (story == null || story.UserId != targetUser.Id || story.ExpiresAt < DateTime.UtcNow)
                {
                    return Failure("Story khong hop le hoac da het han.");
                }
            }

            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var isConversationBlocked = await _context.MessageBlocks
                    .AnyAsync(block =>
                        (block.BlockerUserId == senderUserId && block.BlockedUserId == targetUser.Id) ||
                        (block.BlockerUserId == targetUser.Id && block.BlockedUserId == senderUserId),
                        cancellationToken);

                if (isConversationBlocked)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Failure("Tin nhan da bi chan boi mot trong hai ben.");
                }

                var conversation = await GetOrCreateConversationAsync(senderUserId, targetUser.Id, cancellationToken);
                var createdAt = DateTime.UtcNow;
                conversation.UpdatedAt = createdAt;
                DirectMessageReplyPreviewViewModel? replyTo = null;

                if (model.ReplyToMessageId.HasValue)
                {
                    replyTo = await _context.DirectMessages
                        .Where(message =>
                            message.Id == model.ReplyToMessageId.Value &&
                            message.ConversationId == conversation.Id)
                        .Select(message => new DirectMessageReplyPreviewViewModel
                        {
                            MessageId = message.Id,
                            SenderId = message.SenderId,
                            SenderDisplayName = string.IsNullOrWhiteSpace(message.Sender.FullName)
                                ? message.Sender.Username
                                : message.Sender.FullName,
                            Content = CreateReplyExcerpt(message.Content)
                        })
                        .FirstOrDefaultAsync(cancellationToken);

                    if (replyTo is null)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return Failure("Khong tim thay tin nhan de tra loi trong cuoc tro chuyen nay.");
                    }
                }

                var message = new DirectMessage
                {
                    ConversationId = conversation.Id,
                    SenderId = senderUserId,
                    Content = content,
                    ReplyToMessageId = replyTo?.MessageId,
                    StoryId = model.StoryId,
                    IsStoryReply = model.IsStoryReply,
                    CreatedAt = createdAt
                };

                _context.DirectMessages.Add(message);
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return new DirectMessageSendResult
                {
                    Success = true,
                    ConversationGroupName = DirectMessageChannel.GetConversationGroupName(senderUserId, targetUser.Id),
                    TargetUsername = targetUser.Username,
                    TargetDisplayName = GetDisplayName(targetUser.Username, targetUser.FullName),
                    Message = new DirectMessageRealtimeViewModel
                    {
                        Id = message.Id,
                        ConversationId = conversation.Id,
                        SenderId = senderUserId,
                        SenderDisplayName = GetDisplayName(sender.Username, sender.FullName),
                        Content = content,
                        CreatedAtDisplay = createdAt.ToString("dd MMM, HH:mm"),
                        CreatedAtIso = createdAt.ToString("O"),
                        ReplyTo = replyTo,
                        StoryId = model.StoryId,
                        IsStoryReply = model.IsStoryReply
                    }
                };
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        public static string GetDisplayName(string username, string? fullName)
        {
            return string.IsNullOrWhiteSpace(fullName) ? username : fullName;
        }

        public static DirectMessageReplyPreviewViewModel? MapReplyPreview(DirectMessage? message)
        {
            if (message is null || message.Sender is null)
            {
                return null;
            }

            return new DirectMessageReplyPreviewViewModel
            {
                MessageId = message.Id,
                SenderId = message.SenderId,
                SenderDisplayName = GetDisplayName(message.Sender.Username, message.Sender.FullName),
                Content = CreateReplyExcerpt(message.Content)
            };
        }

        public async Task MarkConversationAsReadAsync(int viewerUserId, int targetUserId, CancellationToken cancellationToken = default)
        {
            var (userAId, userBId) = OrderConversationUsers(viewerUserId, targetUserId);
            var unreadMessages = await _context.DirectMessages
                .Where(message =>
                    message.Conversation.UserAId == userAId &&
                    message.Conversation.UserBId == userBId &&
                    message.SenderId != viewerUserId &&
                    !message.IsRead)
                .ToListAsync(cancellationToken);

            if (unreadMessages.Count == 0)
            {
                return;
            }

            foreach (var unreadMessage in unreadMessages)
            {
                unreadMessage.IsRead = true;
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task<string?> GetConversationAccessErrorAsync(int currentUserId, int targetUserId, CancellationToken cancellationToken = default)
        {
            if (targetUserId <= 0 || targetUserId == currentUserId)
            {
                return "Khong tim thay cuoc tro chuyen hop le.";
            }

            var activeUserIds = await _context.Users
                .Where(user =>
                    user.IsActive &&
                    (user.Id == currentUserId || user.Id == targetUserId))
                .Select(user => user.Id)
                .ToListAsync(cancellationToken);

            if (!activeUserIds.Contains(currentUserId))
            {
                return "Ban can dang nhap de su dung chat realtime.";
            }

            if (!activeUserIds.Contains(targetUserId))
            {
                return "Khong tim thay nguoi dung nhan tin.";
            }

            var isConversationBlocked = await _context.MessageBlocks
                .AnyAsync(block =>
                    (block.BlockerUserId == currentUserId && block.BlockedUserId == targetUserId) ||
                    (block.BlockerUserId == targetUserId && block.BlockedUserId == currentUserId),
                    cancellationToken);

            return isConversationBlocked
                ? "Tin nhan da bi chan boi mot trong hai ben."
                : null;
        }

        private async Task<DirectConversation> GetOrCreateConversationAsync(int userId, int targetUserId, CancellationToken cancellationToken)
        {
            var existingConversation = await GetConversationQuery(userId, targetUserId)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingConversation is not null)
            {
                return existingConversation;
            }

            var (userAId, userBId) = OrderConversationUsers(userId, targetUserId);
            var conversation = new DirectConversation
            {
                UserAId = userAId,
                UserBId = userBId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.DirectConversations.Add(conversation);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return conversation;
            }
            catch (DbUpdateException)
            {
                _context.Entry(conversation).State = EntityState.Detached;

                existingConversation = await GetConversationQuery(userId, targetUserId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (existingConversation is not null)
                {
                    return existingConversation;
                }

                throw;
            }
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

        private static DirectMessageSendResult Failure(string errorMessage)
        {
            return new DirectMessageSendResult
            {
                Success = false,
                ErrorMessage = errorMessage
            };
        }

        private static string CreateReplyExcerpt(string content)
        {
            var normalized = Regex.Replace(content ?? string.Empty, "\\s+", " ").Trim();
            if (normalized.Length <= ReplyPreviewMaxLength)
            {
                return normalized;
            }

            return $"{normalized[..(ReplyPreviewMaxLength - 3)].TrimEnd()}...";
        }
    }

    public static class DirectMessageChannel
    {
        public static string GetConversationGroupName(int firstUserId, int secondUserId)
        {
            var userAId = Math.Min(firstUserId, secondUserId);
            var userBId = Math.Max(firstUserId, secondUserId);
            return $"dm:{userAId}:{userBId}";
        }
    }
}
