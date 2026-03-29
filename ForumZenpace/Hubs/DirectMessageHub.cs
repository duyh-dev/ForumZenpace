using System.Security.Claims;
using ForumZenpace.Models;
using ForumZenpace.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ForumZenpace.Hubs
{
    [Authorize]
    public sealed class DirectMessageHub : Hub
    {
        private readonly DirectMessageService _directMessageService;

        public DirectMessageHub(DirectMessageService directMessageService)
        {
            _directMessageService = directMessageService;
        }

        public async Task JoinConversation(int targetUserId)
        {
            var currentUserId = GetCurrentUserId();
            await JoinConversationGroupAsync(currentUserId, targetUserId);
            await _directMessageService.MarkConversationAsReadAsync(currentUserId, targetUserId);
        }

        public async Task MarkConversationAsRead(int targetUserId)
        {
            var currentUserId = GetCurrentUserId();
            if (targetUserId <= 0 || targetUserId == currentUserId)
            {
                return;
            }

            await _directMessageService.MarkConversationAsReadAsync(currentUserId, targetUserId);
            var conversationGroup = await JoinConversationGroupAsync(currentUserId, targetUserId);
            await Clients.GroupExcept(conversationGroup, Context.ConnectionId)
                .SendAsync("MessagesSeen", currentUserId);
        }

        public async Task SendTypingIndicator(int targetUserId)
        {
            var currentUserId = GetCurrentUserId();
            if (targetUserId <= 0 || targetUserId == currentUserId)
            {
                return;
            }

            var conversationGroup = await JoinConversationGroupAsync(currentUserId, targetUserId);
            await Clients.GroupExcept(conversationGroup, Context.ConnectionId)
                .SendAsync("TypingIndicatorReceived", currentUserId);
        }

        public async Task SendDirectMessage(SendDirectMessageViewModel model)
        {
            var currentUserId = GetCurrentUserId();
            var result = await _directMessageService.SendMessageAsync(currentUserId, model);
            if (!result.Success || result.Message is null)
            {
                throw new HubException(result.ErrorMessage);
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, result.ConversationGroupName);
            await Clients.Group(result.ConversationGroupName)
                .SendAsync("DirectMessageReceived", result.Message);
        }

        public async Task StartVoiceChat(VoiceChatSessionCommandViewModel model)
        {
            var currentUserId = GetCurrentUserId();
            var (conversationGroup, sessionId, _) = await PrepareVoiceChatAsync(currentUserId, model);

            await Clients.GroupExcept(conversationGroup, Context.ConnectionId)
                .SendAsync("VoiceChatIncoming", new
                {
                    sessionId,
                    senderUserId = currentUserId
                });
        }

        public async Task AcceptVoiceChat(VoiceChatSessionCommandViewModel model)
        {
            var currentUserId = GetCurrentUserId();
            var (conversationGroup, sessionId, _) = await PrepareVoiceChatAsync(currentUserId, model);

            await Clients.GroupExcept(conversationGroup, Context.ConnectionId)
                .SendAsync("VoiceChatAccepted", new
                {
                    sessionId,
                    senderUserId = currentUserId
                });
        }

        public async Task RejectVoiceChat(VoiceChatSessionCommandViewModel model)
        {
            var currentUserId = GetCurrentUserId();
            var (conversationGroup, sessionId, reason) = await PrepareVoiceChatAsync(currentUserId, model);

            await Clients.GroupExcept(conversationGroup, Context.ConnectionId)
                .SendAsync("VoiceChatRejected", new
                {
                    sessionId,
                    senderUserId = currentUserId,
                    reason
                });
        }

        public async Task EndVoiceChat(VoiceChatSessionCommandViewModel model)
        {
            var currentUserId = GetCurrentUserId();
            var (conversationGroup, sessionId, reason) = await PrepareVoiceChatAsync(currentUserId, model);

            await Clients.GroupExcept(conversationGroup, Context.ConnectionId)
                .SendAsync("VoiceChatEnded", new
                {
                    sessionId,
                    senderUserId = currentUserId,
                    reason
                });
        }

        public async Task RelayVoiceChatSignal(VoiceChatSignalViewModel model)
        {
            var currentUserId = GetCurrentUserId();
            await EnsureConversationAccessAsync(currentUserId, model.TargetUserId);
            var conversationGroup = await JoinConversationGroupAsync(currentUserId, model.TargetUserId);
            var sessionId = NormalizeSessionId(model.SessionId);
            var signalType = NormalizeSignalType(model.Type);
            if (string.IsNullOrWhiteSpace(signalType))
            {
                throw new HubException("Tin hieu voice chat khong hop le.");
            }

            await Clients.GroupExcept(conversationGroup, Context.ConnectionId)
                .SendAsync("VoiceChatSignalReceived", new
                {
                    sessionId,
                    senderUserId = currentUserId,
                    type = signalType,
                    sdp = model.Sdp,
                    candidate = model.Candidate,
                    sdpMid = model.SdpMid,
                    sdpMLineIndex = model.SdpMLineIndex
                });
        }

        private int GetCurrentUserId()
        {
            if (!int.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                throw new HubException("Ban can dang nhap de su dung chat realtime.");
            }

            return userId;
        }

        private async Task<string> JoinConversationGroupAsync(int currentUserId, int targetUserId)
        {
            if (targetUserId <= 0 || targetUserId == currentUserId)
            {
                throw new HubException("Khong tim thay cuoc tro chuyen hop le.");
            }

            var conversationGroup = DirectMessageChannel.GetConversationGroupName(currentUserId, targetUserId);
            await Groups.AddToGroupAsync(Context.ConnectionId, conversationGroup);
            return conversationGroup;
        }

        private async Task<(string ConversationGroup, string SessionId, string Reason)> PrepareVoiceChatAsync(int currentUserId, VoiceChatSessionCommandViewModel model)
        {
            await EnsureConversationAccessAsync(currentUserId, model.TargetUserId);
            var conversationGroup = await JoinConversationGroupAsync(currentUserId, model.TargetUserId);
            var sessionId = NormalizeSessionId(model.SessionId);
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new HubException("Khong tim thay phien voice chat hop le.");
            }

            return (conversationGroup, sessionId, (model.Reason ?? string.Empty).Trim());
        }

        private static string NormalizeSessionId(string? sessionId)
        {
            return sessionId?.Trim() ?? string.Empty;
        }

        private static string NormalizeSignalType(string? signalType)
        {
            var normalizedType = signalType?.Trim().ToLowerInvariant() ?? string.Empty;
            return normalizedType is "offer" or "answer" or "ice-candidate"
                ? normalizedType
                : string.Empty;
        }

        private async Task EnsureConversationAccessAsync(int currentUserId, int targetUserId)
        {
            var errorMessage = await _directMessageService.GetConversationAccessErrorAsync(currentUserId, targetUserId);
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                throw new HubException(errorMessage);
            }
        }
    }
}
