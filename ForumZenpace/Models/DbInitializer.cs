using System.Data;
using ForumZenpace.Services;
using Microsoft.EntityFrameworkCore;

namespace ForumZenpace.Models
{
    public static class DbInitializer
    {
        private const string ProductVersion = "10.0.5";
        private const string InitialCreateMigrationId = "20260318020427_InitialCreate";
        private const string SeedInitialDataMigrationId = "20260318063635_SeedInitialData";
        private const string AdminEmail = "adminzenpace@gmail.com";
        private const string AdminUsername = "admin";
        private const string AdminDefaultPassword = "gf";

        public static async Task Initialize(ForumDbContext context, PasswordSecurityService passwordSecurityService)
        {
            await EnsureDatabaseSchemaAsync(context);
            await EnsurePasswordStorageAsync(context, passwordSecurityService);
            await EnsureCategoriesAsync(context);

            var admin = await EnsureAdminAccountAsync(context, passwordSecurityService);
            
            await EnsureAdminEmailAsync(context);
            await EnsureWelcomePostAsync(context, admin);
            await EnsureSampleCommunityAsync(context, passwordSecurityService);
        }

        private static async Task EnsureDatabaseSchemaAsync(ForumDbContext context)
        {
            if (!await context.Database.CanConnectAsync())
            {
                await context.Database.MigrateAsync();
                return;
            }

            await AlignMigrationHistoryAsync(context);
            await context.Database.MigrateAsync();
        }

        private static async Task EnsureCategoriesAsync(ForumDbContext context)
        {
            if (await context.Categories.AnyAsync())
            {
                return;
            }

            context.Categories.AddRange(
                new Category { Name = "Lập trình & Kỹ thuật" },
                new Category { Name = "Thiết kế & Nghệ thuật" },
                new Category { Name = "Đời sống & Khoa học" });

            await context.SaveChangesAsync();
        }

        private static async Task<User> EnsureAdminAccountAsync(ForumDbContext context, PasswordSecurityService passwordSecurityService)
        {
            var admin = await context.Users.FirstOrDefaultAsync(user => user.Username == AdminUsername);
            if (admin is not null)
            {
                return admin;
            }

            admin = new User
            {
                Username = AdminUsername,
                Password = passwordSecurityService.HashPassword(AdminDefaultPassword),
                FullName = "Quản trị viên Zenpace",
                Email = AdminEmail,
                IsEmailConfirmed = true,
                RoleId = 1,
                CreatedAt = DateTime.UtcNow
            };

            context.Users.Add(admin);
            await context.SaveChangesAsync();
            return admin;
        }

        private static async Task EnsurePasswordStorageAsync(
    ForumDbContext context,
    PasswordSecurityService passwordSecurityService)
{
    var users = await context.Users
        .Where(u => !string.IsNullOrWhiteSpace(u.Password))
        .ToListAsync();

    var pendingRegistrations = await context.PendingRegistrations
        .Where(p => !string.IsNullOrWhiteSpace(p.Password))
        .ToListAsync();

    var hasChanges = false;

    foreach (var user in users)
    {
        // Nếu password chưa phải hash (ASP.NET Identity hash luôn bắt đầu bằng AQAAAA)
        if (!user.Password.StartsWith("AQAAAA"))
        {
            user.Password = passwordSecurityService.HashPassword(user.Password);
            hasChanges = true;
        }
    }

    foreach (var pending in pendingRegistrations)
    {
        if (!pending.Password.StartsWith("AQAAAA"))
        {
            pending.Password = passwordSecurityService.HashPassword(pending.Password);
            hasChanges = true;
        }
    }

    if (hasChanges)
    {
        await context.SaveChangesAsync();
    }
}

        private static async Task EnsureWelcomePostAsync(ForumDbContext context, User admin)
        {
            var hasWelcomePost = await context.Posts.AnyAsync(post =>
                post.UserId == admin.Id
                && post.Title == "Chào mừng bạn đến với Diễn đàn Zenpace!");

            if (hasWelcomePost)
            {
                return;
            }

            var firstCategory = await context.Categories.OrderBy(category => category.Id).FirstOrDefaultAsync();
            if (firstCategory is null)
            {
                return;
            }

            context.Posts.Add(new Post
            {
                Title = "Chào mừng bạn đến với Diễn đàn Zenpace!",
                Content = "Đây là bài viết đầu tiên được khởi tạo tự động để chào mừng bạn gia nhập cộng đồng Zenpace. Hãy bắt đầu chia sẻ tri thức của bạn tại đây!",
                UserId = admin.Id,
                CategoryId = firstCategory.Id,
                Status = "Active",
                CreatedAt = DateTime.UtcNow,
                ViewCount = 100
            });

            await context.SaveChangesAsync();
        }

        private static async Task EnsureSampleCommunityAsync(ForumDbContext context, PasswordSecurityService passwordSecurityService)
        {
            const string samplePassword = "gf";

            if (await context.Users.AnyAsync(user => user.Username == "linh.dev"))
            {
                return;
            }

            var categories = await context.Categories
                .OrderBy(category => category.Id)
                .ToListAsync();

            if (categories.Count < 3)
            {
                return;
            }

            var passwordHash = passwordSecurityService.HashPassword(samplePassword);
            var now = DateTime.UtcNow;

            var sampleUsers = new[]
            {
                new User
                {
                    Username = "linh.dev",
                    Password = passwordHash,
                    FullName = "Linh Nguyen",
                    Email = "linh.dev@zenpace.local",
                    IsEmailConfirmed = true,
                    RoleId = 2,
                    CreatedAt = now.AddDays(-9)
                },
                new User
                {
                    Username = "minh.design",
                    Password = passwordHash,
                    FullName = "Minh Tran",
                    Email = "minh.design@zenpace.local",
                    IsEmailConfirmed = true,
                    RoleId = 2,
                    CreatedAt = now.AddDays(-8)
                },
                new User
                {
                    Username = "hoa.science",
                    Password = passwordHash,
                    FullName = "Hoa Le",
                    Email = "hoa.science@zenpace.local",
                    IsEmailConfirmed = true,
                    RoleId = 2,
                    CreatedAt = now.AddDays(-7)
                }
            };

            context.Users.AddRange(sampleUsers);
            await context.SaveChangesAsync();

            var linh = sampleUsers[0];
            var minh = sampleUsers[1];
            var hoa = sampleUsers[2];

            var samplePosts = new[]
            {
                new Post
                {
                    Title = "Chia sẻ bộ công cụ học ASP.NET Core hiệu quả",
                    Content = "Mình tổng hợp lại roadmap học ASP.NET Core cho người mới: nắm routing, EF Core, authentication và triển khai từng bước bằng một dự án thật.",
                    UserId = linh.Id,
                    CategoryId = categories[0].Id,
                    Status = "Active",
                    ViewCount = 42,
                    CreatedAt = now.AddDays(-6),
                    UpdatedAt = now.AddDays(-6)
                },
                new Post
                {
                    Title = "Moodboard giao diện diễn đàn tối giản nhưng vẫn ấm áp",
                    Content = "Mình đang thử một hướng visual dùng nền sáng, typography rõ ràng và các khối nội dung thoáng để người đọc tập trung hơn vào bài viết.",
                    UserId = minh.Id,
                    CategoryId = categories[1].Id,
                    Status = "Active",
                    ViewCount = 35,
                    CreatedAt = now.AddDays(-5),
                    UpdatedAt = now.AddDays(-4)
                },
                new Post
                {
                    Title = "Mẹo giữ nhịp học tập bền vững trong 30 ngày",
                    Content = "Thay vì học quá sức trong vài ngày đầu, mình chia thành các phiên 45 phút, ghi lại tiến độ và dành thời gian tổng kết cuối tuần.",
                    UserId = hoa.Id,
                    CategoryId = categories[2].Id,
                    Status = "Active",
                    ViewCount = 58,
                    CreatedAt = now.AddDays(-4),
                    UpdatedAt = now.AddDays(-3)
                }
            };

            context.Posts.AddRange(samplePosts);
            await context.SaveChangesAsync();

            var aspPost = samplePosts[0];
            var designPost = samplePosts[1];
            var habitPost = samplePosts[2];

            var comments = new[]
            {
                new Comment
                {
                    Content = "Bài roadmap này rất dễ theo. Nếu được bạn thêm phần debug EF Core nữa thì quá ổn.",
                    UserId = minh.Id,
                    PostId = aspPost.Id,
                    CreatedAt = now.AddDays(-5)
                },
                new Comment
                {
                    Content = "Mình thích cách chia giai đoạn học theo dự án thật, đỡ bị ngợp hơn hẳn.",
                    UserId = hoa.Id,
                    PostId = aspPost.Id,
                    CreatedAt = now.AddDays(-5).AddHours(2)
                },
                new Comment
                {
                    Content = "Tông màu ấm và khoảng trắng rộng nhìn rất hợp với diễn đàn học thuật.",
                    UserId = linh.Id,
                    PostId = designPost.Id,
                    CreatedAt = now.AddDays(-3)
                },
                new Comment
                {
                    Content = "Mình đã thử phương pháp 45 phút học, 10 phút nghỉ và thấy duy trì tốt hơn.",
                    UserId = linh.Id,
                    PostId = habitPost.Id,
                    CreatedAt = now.AddDays(-2)
                }
            };

            context.Comments.AddRange(comments);
            await context.SaveChangesAsync();

            context.Comments.Add(new Comment
            {
                Content = "Chuẩn luôn, mình sẽ bổ sung thêm checklist debug ở bài tiếp theo.",
                UserId = linh.Id,
                PostId = aspPost.Id,
                ParentId = comments[0].Id,
                CreatedAt = now.AddDays(-5).AddHours(5)
            });

            context.Likes.AddRange(
                new Like { UserId = minh.Id, PostId = aspPost.Id },
                new Like { UserId = hoa.Id, PostId = aspPost.Id },
                new Like { UserId = linh.Id, PostId = designPost.Id },
                new Like { UserId = minh.Id, PostId = habitPost.Id });

            context.CommentLikes.AddRange(
                new CommentLike { UserId = linh.Id, CommentId = comments[0].Id },
                new CommentLike { UserId = hoa.Id, CommentId = comments[2].Id });

            context.Friendships.AddRange(
                new Friendship { UserAId = linh.Id, UserBId = minh.Id, CreatedAt = now.AddDays(-6) },
                new Friendship { UserAId = linh.Id, UserBId = hoa.Id, CreatedAt = now.AddDays(-5) });

            context.DirectConversations.Add(new DirectConversation
            {
                UserAId = linh.Id,
                UserBId = minh.Id,
                CreatedAt = now.AddDays(-2),
                UpdatedAt = now.AddDays(-1)
            });

            context.Notifications.AddRange(
                new Notification
                {
                    UserId = linh.Id,
                    ActorUserId = minh.Id,
                    Content = "Minh đã thích bài viết roadmap ASP.NET Core của bạn.",
                    Type = NotificationTypes.General,
                    IsRead = false,
                    CreatedAt = now.AddDays(-1)
                },
                new Notification
                {
                    UserId = minh.Id,
                    ActorUserId = linh.Id,
                    Content = "Linh đã bình luận về moodboard giao diện của bạn.",
                    Type = NotificationTypes.General,
                    IsRead = true,
                    CreatedAt = now.AddDays(-2)
                });

            await context.SaveChangesAsync();

            var conversation = await context.DirectConversations
                .FirstAsync(item => item.UserAId == linh.Id && item.UserBId == minh.Id);

            context.DirectMessages.AddRange(
                new DirectMessage
                {
                    ConversationId = conversation.Id,
                    SenderId = linh.Id,
                    Content = "Mình vừa xem mockup mới, phần header nhìn rất ổn.",
                    IsRead = true,
                    CreatedAt = now.AddDays(-1).AddHours(-3)
                },
                new DirectMessage
                {
                    ConversationId = conversation.Id,
                    SenderId = minh.Id,
                    Content = "Cảm ơn nhé, mình sẽ tinh chỉnh thêm phần thẻ bài viết.",
                    IsRead = false,
                    CreatedAt = now.AddDays(-1).AddHours(-2)
                });

            await context.SaveChangesAsync();
        }

        private static async Task AlignMigrationHistoryAsync(ForumDbContext context)
        {
            var hasCoreTables = await TableExistsAsync(context, "Posts")
                && await TableExistsAsync(context, "Comments")
                && await TableExistsAsync(context, "Users");

            if (!hasCoreTables)
            {
                return;
            }

            await EnsureMigrationHistoryTableAsync(context);
            await EnsureMigrationHistoryRowAsync(context, InitialCreateMigrationId);

            if (await SeedDataExistsAsync(context))
            {
                await EnsureMigrationHistoryRowAsync(context, SeedInitialDataMigrationId);
            }
        }

        private static async Task EnsureMigrationHistoryTableAsync(ForumDbContext context)
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[__EFMigrationsHistory]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [__EFMigrationsHistory]
                    (
                        [MigrationId] nvarchar(150) NOT NULL,
                        [ProductVersion] nvarchar(32) NOT NULL,
                        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
                    );
                END
                """);
        }

        private static async Task EnsureMigrationHistoryRowAsync(ForumDbContext context, string migrationId)
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = {0})
                BEGIN
                    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                    VALUES ({0}, {1});
                END
                """,
                migrationId,
                ProductVersion);
        }

        private static async Task<bool> SeedDataExistsAsync(ForumDbContext context)
        {
            if (!await TableExistsAsync(context, "Categories"))
            {
                return false;
            }

            var categoryIds = new[] { 1, 2, 3 };
            var seededCategoryCount = await context.Categories.CountAsync(category => categoryIds.Contains(category.Id));
            var hasAdminUser = await context.Users.AnyAsync(user => user.Username == AdminUsername);

            return seededCategoryCount == categoryIds.Length && hasAdminUser;
        }

        private static async Task EnsureAdminEmailAsync(ForumDbContext context)
        {
            var admin = await context.Users.FirstOrDefaultAsync(user => user.Username == AdminUsername);
            if (admin == null || string.Equals(admin.Email, AdminEmail, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            admin.Email = AdminEmail;
            admin.IsEmailConfirmed = false;
            admin.EmailVerificationToken = null;
            admin.EmailVerificationTokenExpiresAt = null;
            admin.PasswordResetToken = null;
            admin.PasswordResetTokenExpiresAt = null;

            await context.SaveChangesAsync();
        }

        private static async Task<bool> TableExistsAsync(ForumDbContext context, string tableName)
        {
            var connection = context.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;

            if (shouldClose)
            {
                await connection.OpenAsync();
            }

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT 1
                    FROM sys.tables WITH (NOLOCK)
                    WHERE name = @tableName AND schema_id = SCHEMA_ID('dbo')
                    """;

                var parameter = command.CreateParameter();
                parameter.ParameterName = "@tableName";
                parameter.Value = tableName;
                command.Parameters.Add(parameter);

                var result = await command.ExecuteScalarAsync();
                return result != null && result != DBNull.Value;
            }
            finally
            {
                if (shouldClose)
                {
                    await connection.CloseAsync();
                }
            }
        }
    }
}
