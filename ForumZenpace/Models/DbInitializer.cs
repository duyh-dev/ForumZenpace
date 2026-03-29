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
