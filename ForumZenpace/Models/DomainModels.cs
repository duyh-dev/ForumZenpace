using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ForumZenpace.Models
{
    public class Role
    {
        public int Id { get; set; }
        
        [Required, MaxLength(50)]
        public string Name { get; set; } = string.Empty;
        
        public ICollection<User> Users { get; set; } = new List<User>();
    }

    public class User
    {
        public int Id { get; set; }
        
        [Required, MaxLength(50)]
        public string Username { get; set; } = string.Empty;
        
        [Required]
        public string Password { get; set; } = string.Empty;
        
        [Required, MaxLength(100)]
        public string FullName { get; set; } = string.Empty;
        
        [Required, EmailAddress, MaxLength(100)]
        public string Email { get; set; } = string.Empty;

        public bool IsEmailConfirmed { get; set; }

        [MaxLength(128)]
        public string? EmailVerificationToken { get; set; }

        public DateTime? EmailVerificationTokenExpiresAt { get; set; }

        [MaxLength(128)]
        public string? PasswordResetToken { get; set; }

        public DateTime? PasswordResetTokenExpiresAt { get; set; }
        
        [MaxLength(255)]
        public string? Avatar { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public bool IsActive { get; set; } = true;

        public int RoleId { get; set; }
        [ForeignKey("RoleId")]
        public Role Role { get; set; } = null!;

        public ICollection<Post> Posts { get; set; } = new List<Post>();
        public ICollection<Comment> Comments { get; set; } = new List<Comment>();
        public ICollection<Like> Likes { get; set; } = new List<Like>();
        public ICollection<CommentLike> CommentLikes { get; set; } = new List<CommentLike>();
        public ICollection<PostImage> PostImages { get; set; } = new List<PostImage>();
        public ICollection<Report> Reports { get; set; } = new List<Report>();
        public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
        public ICollection<Notification> ActorNotifications { get; set; } = new List<Notification>();
        public ICollection<DirectConversation> PrimaryDirectConversations { get; set; } = new List<DirectConversation>();
        public ICollection<DirectConversation> SecondaryDirectConversations { get; set; } = new List<DirectConversation>();
        public ICollection<DirectMessage> DirectMessages { get; set; } = new List<DirectMessage>();
        public ICollection<FriendRequest> SentFriendRequests { get; set; } = new List<FriendRequest>();
        public ICollection<FriendRequest> ReceivedFriendRequests { get; set; } = new List<FriendRequest>();
        public ICollection<Friendship> PrimaryFriendships { get; set; } = new List<Friendship>();
        public ICollection<Friendship> SecondaryFriendships { get; set; } = new List<Friendship>();
        public ICollection<MessageBlock> SentMessageBlocks { get; set; } = new List<MessageBlock>();
        public ICollection<MessageBlock> ReceivedMessageBlocks { get; set; } = new List<MessageBlock>();
        public ICollection<Story> Stories { get; set; } = new List<Story>();
        public ICollection<StoryView> StoryViews { get; set; } = new List<StoryView>();

        /// <summary>Averaged embedding vector of liked posts, stored as JSON float array.</summary>
        public string? PreferenceVectorData { get; set; }
    }

    public class PendingRegistration
    {
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(100)]
        public string Email { get; set; } = string.Empty;

        [Required, MaxLength(128)]
        public string OtpHash { get; set; } = string.Empty;

        public DateTime OtpExpiresAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Category
    {
        public int Id { get; set; }
        
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public ICollection<Post> Posts { get; set; } = new List<Post>();
    }

    public class Post
    {
        public int Id { get; set; }
        
        [Required, MaxLength(255)]
        public string Title { get; set; } = string.Empty;
        
        [Required]
        public string Content { get; set; } = string.Empty;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        
        public int ViewCount { get; set; } = 0;
        
        public string Status { get; set; } = "Active"; // Active, Hidden

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public User User { get; set; } = null!;

        public int CategoryId { get; set; }
        [ForeignKey("CategoryId")]
        public Category Category { get; set; } = null!;

        public ICollection<Comment> Comments { get; set; } = new List<Comment>();
        public ICollection<Like> Likes { get; set; } = new List<Like>();
        public ICollection<PostImage> Images { get; set; } = new List<PostImage>();
        public ICollection<Report> Reports { get; set; } = new List<Report>();

        /// <summary>768-dimensional embedding vector from Gemini, stored as JSON float array.</summary>
        public string? VectorData { get; set; }
    }

    public class Comment
    {
        public int Id { get; set; }
        
        [Required]
        public string Content { get; set; } = string.Empty;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public User User { get; set; } = null!;

        public int PostId { get; set; }
        [ForeignKey("PostId")]
        public Post Post { get; set; } = null!;

        public int? ParentId { get; set; }
        [ForeignKey("ParentId")]
        public Comment? ParentComment { get; set; }
        
        public ICollection<Comment> Replies { get; set; } = new List<Comment>();
        public ICollection<CommentLike> CommentLikes { get; set; } = new List<CommentLike>();
    }

    public class Like
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public User User { get; set; } = null!;

        public int PostId { get; set; }
        [ForeignKey("PostId")]
        public Post Post { get; set; } = null!;
    }

    public class CommentLike
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public User User { get; set; } = null!;

        public int CommentId { get; set; }
        [ForeignKey("CommentId")]
        public Comment Comment { get; set; } = null!;
    }

    public class PostImage
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public User User { get; set; } = null!;

        public int? PostId { get; set; }
        [ForeignKey("PostId")]
        public Post? Post { get; set; }

        [MaxLength(64)]
        public string? DraftToken { get; set; }

        [Required, MaxLength(255)]
        public string FileName { get; set; } = string.Empty;

        [Required, MaxLength(255)]
        public string OriginalFileName { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string ContentType { get; set; } = string.Empty;

        [Required, MaxLength(255)]
        public string ImageUrl { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Story
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public User User { get; set; } = null!;

        [MaxLength(1200)]
        public string? TextContent { get; set; }

        [Required, MaxLength(40)]
        public string BackgroundStyle { get; set; } = StoryBackgroundStyles.Aurora;

        [MaxLength(255)]
        public string? ImageFileName { get; set; }

        [MaxLength(255)]
        public string? ImageOriginalFileName { get; set; }

        [MaxLength(100)]
        public string? ImageContentType { get; set; }

        [MaxLength(255)]
        public string? ImageUrl { get; set; }

        [MaxLength(255)]
        public string? MusicFileName { get; set; }

        [MaxLength(255)]
        public string? MusicOriginalFileName { get; set; }

        [MaxLength(100)]
        public string? MusicContentType { get; set; }

        [MaxLength(255)]
        public string? MusicUrl { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);

        public ICollection<StoryView> Views { get; set; } = new List<StoryView>();
    }

    public class StoryView
    {
        public int Id { get; set; }

        public int StoryId { get; set; }
        [ForeignKey("StoryId")]
        public Story Story { get; set; } = null!;

        public int ViewerUserId { get; set; }
        [ForeignKey("ViewerUserId")]
        public User ViewerUser { get; set; } = null!;

        public DateTime ViewedAt { get; set; } = DateTime.UtcNow;
    }

    public class Report
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public User User { get; set; } = null!;

        public int PostId { get; set; }
        [ForeignKey("PostId")]
        public Post Post { get; set; } = null!;

        [Required, MaxLength(255)]
        public string Reason { get; set; } = string.Empty;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Notification
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public User User { get; set; } = null!;

        [Required]
        public string Content { get; set; } = string.Empty;

        [Required, MaxLength(40)]
        public string Type { get; set; } = NotificationTypes.General;

        public int? ActorUserId { get; set; }
        [ForeignKey("ActorUserId")]
        public User? ActorUser { get; set; }

        public int? FriendRequestId { get; set; }
        [ForeignKey("FriendRequestId")]
        public FriendRequest? FriendRequest { get; set; }

        public int? StoryId { get; set; }
        [ForeignKey("StoryId")]
        public Story? Story { get; set; }

        public bool IsRead { get; set; } = false;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class FriendRequest
    {
        public int Id { get; set; }

        public int SenderId { get; set; }
        [ForeignKey("SenderId")]
        public User Sender { get; set; } = null!;

        public int ReceiverId { get; set; }
        [ForeignKey("ReceiverId")]
        public User Receiver { get; set; } = null!;

        [Required, MaxLength(20)]
        public string Status { get; set; } = FriendRequestStatuses.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? RespondedAt { get; set; }
    }

    public class Friendship
    {
        public int Id { get; set; }

        public int UserAId { get; set; }
        [ForeignKey("UserAId")]
        public User UserA { get; set; } = null!;

        public int UserBId { get; set; }
        [ForeignKey("UserBId")]
        public User UserB { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class MessageBlock
    {
        public int Id { get; set; }

        public int BlockerUserId { get; set; }
        [ForeignKey("BlockerUserId")]
        public User BlockerUser { get; set; } = null!;

        public int BlockedUserId { get; set; }
        [ForeignKey("BlockedUserId")]
        public User BlockedUser { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class DirectConversation
    {
        public int Id { get; set; }

        public int UserAId { get; set; }
        [ForeignKey("UserAId")]
        public User UserA { get; set; } = null!;

        public int UserBId { get; set; }
        [ForeignKey("UserBId")]
        public User UserB { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<DirectMessage> Messages { get; set; } = new List<DirectMessage>();
    }

    public class DirectMessage
    {
        public int Id { get; set; }

        public int ConversationId { get; set; }
        [ForeignKey("ConversationId")]
        public DirectConversation Conversation { get; set; } = null!;

        public int SenderId { get; set; }
        [ForeignKey("SenderId")]
        public User Sender { get; set; } = null!;

        [Required, MaxLength(1000)]
        public string Content { get; set; } = string.Empty;

        public int? ReplyToMessageId { get; set; }
        [ForeignKey("ReplyToMessageId")]
        public DirectMessage? ReplyToMessage { get; set; }

        public int? StoryId { get; set; }
        [ForeignKey("StoryId")]
        public Story? Story { get; set; }

        public bool IsStoryReply { get; set; }

        public bool IsRead { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<DirectMessage> Replies { get; set; } = new List<DirectMessage>();
    }

    public static class FriendRequestStatuses
    {
        public const string Pending = "Pending";
        public const string Accepted = "Accepted";
        public const string Declined = "Declined";
        public const string Cancelled = "Cancelled";
    }

    public static class NotificationTypes
    {
        public const string General = "General";
        public const string FriendRequest = "FriendRequest";
        public const string FriendAccepted = "FriendAccepted";
        public const string StoryPublished = "StoryPublished";
    }

    public static class StoryBackgroundStyles
    {
        public const string Aurora = "aurora";
        public const string Sunset = "sunset";
        public const string Lagoon = "lagoon";
        public const string Midnight = "midnight";
    }
}
