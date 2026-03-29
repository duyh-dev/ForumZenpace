using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ForumZenpace.Models
{
    public class LoginViewModel
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }

    public class RegisterViewModel
    {
        [Required, MaxLength(50)]
        public string Username { get; set; } = string.Empty;
        
        [Required, MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(100)]
        public string Email { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
        
        [Required, DataType(DataType.Password), Compare("Password", ErrorMessage = "Passwords do not match")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class VerifyEmailOtpViewModel
    {
        [Required]
        public string FlowToken { get; set; } = string.Empty;
        public string EmailMask { get; set; } = string.Empty;

        [Required, Display(Name = "Ma OTP")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Ma OTP gom 6 chu so.")]
        public string OtpCode { get; set; } = string.Empty;
    }

    public class VerifyRegistrationOtpViewModel
    {
        [Required]
        public string FlowToken { get; set; } = string.Empty;
        public string EmailMask { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;

        [Required, Display(Name = "Ma OTP")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Ma OTP gom 6 chu so.")]
        public string OtpCode { get; set; } = string.Empty;
    }

    public class PostViewModel
    {
        public int? PostId { get; set; }

        [MaxLength(64)]
        public string DraftToken { get; set; } = string.Empty;

        [Required, MaxLength(255)]
        public string Title { get; set; } = string.Empty;
        
        [Required]
        public string Content { get; set; } = string.Empty;
        
        [Required]
        public int CategoryId { get; set; }
    }

    public class PostImageUploadViewModel
    {
        public int? PostId { get; set; }

        [MaxLength(64)]
        public string? DraftToken { get; set; }

        [Required]
        public IFormFile Image { get; set; } = null!;
    }

    public class CommentViewModel
    {
        [Required]
        public string Content { get; set; } = string.Empty;
        public int PostId { get; set; }
        public int? ParentId { get; set; }
    }

    public class CommentThreadViewModel
    {
        [Required]
        public Comment RootComment { get; set; } = null!;

        public IReadOnlyList<CommentReplyViewModel> Replies { get; set; } = Array.Empty<CommentReplyViewModel>();

        public int PostId { get; set; }
        public int? CurrentUserId { get; set; }
        public bool IsAuthenticated { get; set; }
        public int InitialVisibleReplies { get; set; } = 3;
    }

    public class CommentReplyViewModel
    {
        [Required]
        public Comment Comment { get; set; } = null!;

        public int Depth { get; set; }
        public string? ReplyingToAuthorName { get; set; }
    }

    public class HomeIndexViewModel
    {
        public IReadOnlyList<Post> Posts { get; set; } = Array.Empty<Post>();
        public IReadOnlyList<Category> Categories { get; set; } = Array.Empty<Category>();
        public string CurrentSort { get; set; } = string.Empty;
        public string? SearchString { get; set; }
        public int? CurrentCategoryId { get; set; }
        public int? CurrentUserId { get; set; }
        public bool IsRecommendedSort { get; set; }
        public int UnreadNotificationCount { get; set; }
        public CurrentUserStorySummaryViewModel? CurrentUserStory { get; set; }
        public IReadOnlyList<FriendSummaryViewModel> Friends { get; set; } = Array.Empty<FriendSummaryViewModel>();
        public IReadOnlyList<FriendCandidateViewModel> SuggestedFriends { get; set; } = Array.Empty<FriendCandidateViewModel>();
        public int SuggestionInsertAfterPost { get; set; } = 3;
    }

    public class CommentItemViewModel
    {
        [Required]
        public Comment Comment { get; set; } = null!;

        public int PostId { get; set; }
        public int? CurrentUserId { get; set; }
        public bool IsAuthenticated { get; set; }
        public bool IsReply { get; set; }
        public string? ReplyingToAuthorName { get; set; }
    }

    public class ProfileViewModel
    {
        [Required, MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(100)]
        public string Email { get; set; } = string.Empty;

        public string? Avatar { get; set; }
        public IFormFile? AvatarFile { get; set; }
        
        // Expose username just for display
        public string? Username { get; set; }
        public int ProfileUserId { get; set; }
        public int? ViewerUserId { get; set; }
        public bool IsOwner { get; set; }
        public bool IsAuthenticatedViewer { get; set; }
        public string ActiveTab { get; set; } = "posts";
        public bool ShowStoriesTab { get; set; }
        public bool ShowChatTab { get; set; }
        public bool CanSendMessages { get; set; }
        public bool IsFriend { get; set; }
        public bool HasIncomingFriendRequest { get; set; }
        public bool HasOutgoingFriendRequest { get; set; }
        public int? IncomingFriendRequestId { get; set; }
        public bool IsMessageBlockedByViewer { get; set; }
        public bool IsMessageBlockedByOtherUser { get; set; }
        public bool IsConversationBlocked { get; set; }
        public string ChatAvailabilityMessage { get; set; } = string.Empty;
        public int ChatMessageCount { get; set; }
        public IReadOnlyList<ProfileChatMessageViewModel> ChatMessages { get; set; } = Array.Empty<ProfileChatMessageViewModel>();
        public DateTime JoinedAt { get; set; }
        public int PostCount { get; set; }
        public int StoryCount { get; set; }
        public int ActiveStoryCount { get; set; }
        public int ArchivedStoryCount { get; set; }
        public int TotalViewCount { get; set; }
        public int TotalCommentCount { get; set; }
        public IReadOnlyList<ProfilePostSummaryViewModel> Posts { get; set; } = Array.Empty<ProfilePostSummaryViewModel>();
        public IReadOnlyList<ProfileStorySummaryViewModel> Stories { get; set; } = Array.Empty<ProfileStorySummaryViewModel>();
    }

    public class ProfileChatMessageViewModel
    {
        public int Id { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public bool IsOwnMessage { get; set; }
        public string SenderDisplayName { get; set; } = string.Empty;
        public DirectMessageReplyPreviewViewModel? ReplyTo { get; set; }
    }

    public class DirectMessageReplyPreviewViewModel
    {
        public int MessageId { get; set; }
        public int SenderId { get; set; }
        public string SenderDisplayName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    public class SendDirectMessageViewModel
    {
        public int TargetUserId { get; set; }

        [Required, MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required, MaxLength(1000)]
        public string Content { get; set; } = string.Empty;

        public int? ReplyToMessageId { get; set; }
        public int? StoryId { get; set; }
        public bool IsStoryReply { get; set; }
    }

    public class DirectMessageRealtimeViewModel
    {
        public int Id { get; set; }
        public int ConversationId { get; set; }
        public int SenderId { get; set; }
        public string SenderDisplayName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string CreatedAtDisplay { get; set; } = string.Empty;
        public string CreatedAtIso { get; set; } = string.Empty;
        public DirectMessageReplyPreviewViewModel? ReplyTo { get; set; }
        public int? StoryId { get; set; }
        public bool IsStoryReply { get; set; }
    }

    public class DirectMessageSendResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string ConversationGroupName { get; set; } = string.Empty;
        public string TargetUsername { get; set; } = string.Empty;
        public string TargetDisplayName { get; set; } = string.Empty;
        public DirectMessageRealtimeViewModel? Message { get; set; }
    }

    public class VoiceChatSessionCommandViewModel
    {
        public int TargetUserId { get; set; }

        [Required, MaxLength(64)]
        public string SessionId { get; set; } = string.Empty;

        [MaxLength(200)]
        public string Reason { get; set; } = string.Empty;
    }

    public class VoiceChatSignalViewModel
    {
        public int TargetUserId { get; set; }

        [Required, MaxLength(64)]
        public string SessionId { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string Type { get; set; } = string.Empty;

        public string? Sdp { get; set; }
        public string? Candidate { get; set; }
        public string? SdpMid { get; set; }
        public int? SdpMLineIndex { get; set; }
    }

    public class FriendSummaryViewModel
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public bool IsMessageBlockedByViewer { get; set; }
        public bool IsMessageBlockedByOtherUser { get; set; }
        public bool HasActiveStory { get; set; }
        public bool HasUnviewedStory { get; set; }
        public int ActiveStoryCount { get; set; }
        public int? LatestStoryId { get; set; }
    }

    public class FriendCandidateViewModel
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public string RelationshipState { get; set; } = string.Empty;
        public bool CanSendRequest { get; set; }
        public string ActionLabel { get; set; } = string.Empty;
    }

    public class ForgotPasswordViewModel
    {
        [Required, Display(Name = "Ten tai khoan hoac email")]
        public string Identifier { get; set; } = string.Empty;
    }

    public class ResetPasswordViewModel
    {
        [Required]
        public string FlowToken { get; set; } = string.Empty;

        public string EmailMask { get; set; } = string.Empty;

        [Required, Display(Name = "Ma OTP")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Ma OTP gom 6 chu so.")]
        public string OtpCode { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), Compare("NewPassword", ErrorMessage = "Mat khau moi khong khop.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class NotificationItemViewModel
    {
        public int Id { get; set; }
        public string Type { get; set; } = NotificationTypes.General;
        public string Content { get; set; } = string.Empty;
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? ActorUserId { get; set; }
        public string ActorUsername { get; set; } = string.Empty;
        public string ActorDisplayName { get; set; } = string.Empty;
        public string? ActorAvatarUrl { get; set; }
        public int? FriendRequestId { get; set; }
        public int? StoryId { get; set; }
        public string TargetUrl { get; set; } = string.Empty;
        public string ActionLabel { get; set; } = string.Empty;
        public string FriendRequestStatus { get; set; } = string.Empty;
        public bool CanAcceptFriendRequest { get; set; }
        public bool CanDeclineFriendRequest { get; set; }
    }

    public class NotificationPageViewModel
    {
        public int CurrentUserId { get; set; }
        public int UnreadCount { get; set; }
        public IReadOnlyList<NotificationItemViewModel> Items { get; set; } = Array.Empty<NotificationItemViewModel>();
    }

    public class SendFriendRequestResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public int TargetUserId { get; set; }
        public NotificationItemViewModel? ReceiverNotification { get; set; }
        public int ReceiverUnreadNotificationCount { get; set; }
    }

    public class AcceptFriendRequestResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public int RequestId { get; set; }
        public int SenderUserId { get; set; }
        public int ReceiverUserId { get; set; }
        public FriendSummaryViewModel? FriendForReceiver { get; set; }
        public FriendSummaryViewModel? FriendForSender { get; set; }
        public NotificationItemViewModel? SenderNotification { get; set; }
        public int SenderUnreadNotificationCount { get; set; }
        public int ReceiverUnreadNotificationCount { get; set; }
    }

    public class DeclineFriendRequestResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public int RequestId { get; set; }
        public int SenderUserId { get; set; }
        public int ReceiverUnreadNotificationCount { get; set; }
    }

    public class RemoveFriendResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public int FriendUserId { get; set; }
    }

    public class ToggleMessageBlockResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public int TargetUserId { get; set; }
        public bool IsMessageBlockedByViewer { get; set; }
        public bool IsMessageBlockedByOtherUser { get; set; }
        public bool IsConversationBlocked { get; set; }
    }

    public class RelationshipStatusViewModel
    {
        public bool IsFriend { get; set; }
        public bool HasIncomingFriendRequest { get; set; }
        public bool HasOutgoingFriendRequest { get; set; }
        public int? IncomingFriendRequestId { get; set; }
        public bool IsMessageBlockedByViewer { get; set; }
        public bool IsMessageBlockedByOtherUser { get; set; }
        public bool IsConversationBlocked { get; set; }
    }

    public class ProfilePostSummaryViewModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Excerpt { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int CommentCount { get; set; }
        public int ViewCount { get; set; }
    }

    public class CreateStoryViewModel
    {
        [MaxLength(1200)]
        public string? TextContent { get; set; }

        [MaxLength(40)]
        public string BackgroundStyle { get; set; } = StoryBackgroundStyles.Aurora;

        public IFormFile? Image { get; set; }

        public IFormFile? MusicFile { get; set; }

        [MaxLength(255)]
        public string? SelectedMusicTrackKey { get; set; }

        [MaxLength(500)]
        public string? MusicExternalUrl { get; set; }

        [MaxLength(120)]
        public string? MusicExternalTitle { get; set; }

        [MaxLength(120)]
        public string? MusicExternalArtist { get; set; }

        /// <summary>Start offset in seconds (for uploaded music files, passed to FFmpeg -ss).</summary>
        [Range(0, 600)]
        public int MusicStartTime { get; set; } = 0;

        /// <summary>Duration (clip length) in seconds, clamped to 5–60 on the server.</summary>
        [Range(5, 60)]
        public int MusicDuration { get; set; } = 30;
    }

    public class CurrentUserStorySummaryViewModel
    {
        public bool HasActiveStory { get; set; }
        public int ActiveStoryCount { get; set; }
        public int? LatestStoryId { get; set; }
    }

    public class ProfileStorySummaryViewModel
    {
        public int Id { get; set; }
        public int AuthorUserId { get; set; }
        public string AuthorUsername { get; set; } = string.Empty;
        public string AuthorDisplayName { get; set; } = string.Empty;
        public string? AuthorAvatarUrl { get; set; }
        public string? TextContent { get; set; }
        public string PreviewText { get; set; } = string.Empty;
        public string BackgroundStyle { get; set; } = StoryBackgroundStyles.Aurora;
        public string? ImageUrl { get; set; }
        public bool HasImage { get; set; }
        public string? MusicUrl { get; set; }
        public string MusicDisplayName { get; set; } = string.Empty;
        public bool HasMusic { get; set; }
        public bool ShowInlineMusicPlayer { get; set; }
        public bool CanEmbedMusic { get; set; }
        public string MusicEmbedUrl { get; set; } = string.Empty;
        public string MusicPlayerKind { get; set; } = string.Empty;
        public string MusicPlayerKey { get; set; } = string.Empty;
        public string MusicPlayerUri { get; set; } = string.Empty;
        public bool CanSeekMusic { get; set; }
        public string MusicSourceLabel { get; set; } = string.Empty;
        public string MusicActionUrl { get; set; } = string.Empty;
        public string MusicActionLabel { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public bool IsExpired { get; set; }
        public bool HasBeenViewedByViewer { get; set; }
        public int ViewCount { get; set; }
    }

    public class StoryViewerPageViewModel
    {
        public int CurrentUserId { get; set; }
        public bool IsOwner { get; set; }
        public bool CanManage { get; set; }
        public string ReturnProfileUsername { get; set; } = string.Empty;
        public ProfileStorySummaryViewModel Story { get; set; } = null!;
        public IReadOnlyList<StorySequenceItemViewModel> Sequence { get; set; } = Array.Empty<StorySequenceItemViewModel>();
        public int? PreviousStoryId { get; set; }
        public int? NextStoryId { get; set; }
    }

    public class StorySequenceItemViewModel
    {
        public int Id { get; set; }
        public bool IsCurrent { get; set; }
        public bool IsExpired { get; set; }
        public bool HasBeenViewedByViewer { get; set; }
        public string? ImageUrl { get; set; }
        public string? MusicUrl { get; set; }
    }

    public class StoryMusicTrackOptionViewModel
    {
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string DisplayLabel { get; set; } = string.Empty;
        public string AudioUrl { get; set; } = string.Empty;
        public string ContentType { get; set; } = "application/octet-stream";
    }
}
