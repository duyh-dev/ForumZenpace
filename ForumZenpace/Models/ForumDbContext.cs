using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace ForumZenpace.Models
{
    public class ForumDbContext : DbContext
    {
        public ForumDbContext(DbContextOptions<ForumDbContext> options)
            : base(options)
        {
        }

        public DbSet<Role> Roles { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<PendingRegistration> PendingRegistrations { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Post> Posts { get; set; }
        public DbSet<Comment> Comments { get; set; }
        public DbSet<Like> Likes { get; set; }
        public DbSet<CommentLike> CommentLikes { get; set; }
        public DbSet<PostImage> PostImages { get; set; }
        public DbSet<Story> Stories { get; set; }
        public DbSet<StoryView> StoryViews { get; set; }
        public DbSet<Report> Reports { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<FriendRequest> FriendRequests { get; set; }
        public DbSet<Friendship> Friendships { get; set; }
        public DbSet<MessageBlock> MessageBlocks { get; set; }
        public DbSet<DirectConversation> DirectConversations { get; set; }
        public DbSet<DirectMessage> DirectMessages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Like>()
                .HasIndex(like => new { like.UserId, like.PostId })
                .IsUnique();

            modelBuilder.Entity<CommentLike>()
                .HasIndex(commentLike => new { commentLike.UserId, commentLike.CommentId })
                .IsUnique();

            modelBuilder.Entity<PendingRegistration>()
                .HasIndex(pendingRegistration => pendingRegistration.Username)
                .IsUnique();

            modelBuilder.Entity<PendingRegistration>()
                .HasIndex(pendingRegistration => pendingRegistration.Email)
                .IsUnique();

            modelBuilder.Entity<PostImage>()
                .HasIndex(postImage => postImage.DraftToken);

            modelBuilder.Entity<Story>()
                .HasIndex(story => new { story.UserId, story.CreatedAt });

            modelBuilder.Entity<Story>()
                .HasIndex(story => story.ExpiresAt);

            modelBuilder.Entity<StoryView>()
                .HasIndex(storyView => new { storyView.StoryId, storyView.ViewerUserId })
                .IsUnique();

            modelBuilder.Entity<DirectConversation>()
                .HasIndex(conversation => new { conversation.UserAId, conversation.UserBId })
                .IsUnique();

            modelBuilder.Entity<Friendship>()
                .HasIndex(friendship => new { friendship.UserAId, friendship.UserBId })
                .IsUnique();

            modelBuilder.Entity<FriendRequest>()
                .HasIndex(friendRequest => new { friendRequest.ReceiverId, friendRequest.Status, friendRequest.CreatedAt });

            modelBuilder.Entity<MessageBlock>()
                .HasIndex(block => new { block.BlockerUserId, block.BlockedUserId })
                .IsUnique();

            modelBuilder.Entity<DirectMessage>()
                .HasIndex(message => new { message.ConversationId, message.CreatedAt });

            modelBuilder.Entity<DirectMessage>()
                .HasIndex(message => message.ReplyToMessageId);

            modelBuilder.Entity<DirectConversation>()
                .HasOne(conversation => conversation.UserA)
                .WithMany(user => user.PrimaryDirectConversations)
                .HasForeignKey(conversation => conversation.UserAId);

            modelBuilder.Entity<DirectConversation>()
                .HasOne(conversation => conversation.UserB)
                .WithMany(user => user.SecondaryDirectConversations)
                .HasForeignKey(conversation => conversation.UserBId);

            modelBuilder.Entity<DirectMessage>()
                .HasOne(message => message.Conversation)
                .WithMany(conversation => conversation.Messages)
                .HasForeignKey(message => message.ConversationId);

            modelBuilder.Entity<DirectMessage>()
                .HasOne(message => message.Sender)
                .WithMany(user => user.DirectMessages)
                .HasForeignKey(message => message.SenderId);

            modelBuilder.Entity<DirectMessage>()
                .HasOne(message => message.ReplyToMessage)
                .WithMany(message => message.Replies)
                .HasForeignKey(message => message.ReplyToMessageId);

            modelBuilder.Entity<Notification>()
                .HasOne(notification => notification.ActorUser)
                .WithMany(user => user.ActorNotifications)
                .HasForeignKey(notification => notification.ActorUserId);

            modelBuilder.Entity<Notification>()
                .HasOne(notification => notification.FriendRequest)
                .WithMany()
                .HasForeignKey(notification => notification.FriendRequestId);

            modelBuilder.Entity<Notification>()
                .HasOne(notification => notification.Story)
                .WithMany()
                .HasForeignKey(notification => notification.StoryId);

            modelBuilder.Entity<Notification>()
                .Property(notification => notification.Type)
                .HasDefaultValue(NotificationTypes.General);

            modelBuilder.Entity<Story>()
                .Property(story => story.BackgroundStyle)
                .HasDefaultValue(StoryBackgroundStyles.Aurora);

            modelBuilder.Entity<User>()
                .Property(user => user.IsEmailConfirmed)
                .HasDefaultValue(false);

            modelBuilder.Entity<Story>()
                .HasOne(story => story.User)
                .WithMany(user => user.Stories)
                .HasForeignKey(story => story.UserId);

            modelBuilder.Entity<StoryView>()
                .HasOne(storyView => storyView.Story)
                .WithMany(story => story.Views)
                .HasForeignKey(storyView => storyView.StoryId);

            modelBuilder.Entity<StoryView>()
                .HasOne(storyView => storyView.ViewerUser)
                .WithMany(user => user.StoryViews)
                .HasForeignKey(storyView => storyView.ViewerUserId);

            modelBuilder.Entity<FriendRequest>()
                .HasOne(friendRequest => friendRequest.Sender)
                .WithMany(user => user.SentFriendRequests)
                .HasForeignKey(friendRequest => friendRequest.SenderId);

            modelBuilder.Entity<FriendRequest>()
                .HasOne(friendRequest => friendRequest.Receiver)
                .WithMany(user => user.ReceivedFriendRequests)
                .HasForeignKey(friendRequest => friendRequest.ReceiverId);

            modelBuilder.Entity<Friendship>()
                .HasOne(friendship => friendship.UserA)
                .WithMany(user => user.PrimaryFriendships)
                .HasForeignKey(friendship => friendship.UserAId);

            modelBuilder.Entity<Friendship>()
                .HasOne(friendship => friendship.UserB)
                .WithMany(user => user.SecondaryFriendships)
                .HasForeignKey(friendship => friendship.UserBId);

            modelBuilder.Entity<MessageBlock>()
                .HasOne(block => block.BlockerUser)
                .WithMany(user => user.SentMessageBlocks)
                .HasForeignKey(block => block.BlockerUserId);

            modelBuilder.Entity<MessageBlock>()
                .HasOne(block => block.BlockedUser)
                .WithMany(user => user.ReceivedMessageBlocks)
                .HasForeignKey(block => block.BlockedUserId);

            foreach (var relationship in modelBuilder.Model.GetEntityTypes().SelectMany(entityType => entityType.GetForeignKeys()))
            {
                relationship.DeleteBehavior = DeleteBehavior.Restrict;
            }

            modelBuilder.Entity<Category>().HasData(
                new Category { Id = 1, Name = "Lập trình & Kỹ thuật" },
                new Category { Id = 2, Name = "Thiết kế & Nghệ thuật" },
                new Category { Id = 3, Name = "Đời sống & Khoa học" }
            );

            modelBuilder.Entity<Role>().HasData(
                new Role { Id = 1, Name = "Admin" },
                new Role { Id = 2, Name = "User" }
            );
        }
    }
}
