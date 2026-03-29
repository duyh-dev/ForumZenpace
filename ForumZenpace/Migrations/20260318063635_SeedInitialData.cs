using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ForumZenpace.Migrations
{
    /// <inheritdoc />
    public partial class SeedInitialData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET IDENTITY_INSERT [Categories] ON");
            migrationBuilder.Sql(
                """
                IF NOT EXISTS (SELECT 1 FROM [Categories] WHERE [Id] = 1)
                BEGIN
                    INSERT INTO [Categories] ([Id], [Name]) VALUES (1, N'Lập trình & Kỹ thuật');
                END

                IF NOT EXISTS (SELECT 1 FROM [Categories] WHERE [Id] = 2)
                BEGIN
                    INSERT INTO [Categories] ([Id], [Name]) VALUES (2, N'Thiết kế & Nghệ thuật');
                END

                IF NOT EXISTS (SELECT 1 FROM [Categories] WHERE [Id] = 3)
                BEGIN
                    INSERT INTO [Categories] ([Id], [Name]) VALUES (3, N'Đời sống & Khoa học');
                END
                """);
            migrationBuilder.Sql("SET IDENTITY_INSERT [Categories] OFF");

            migrationBuilder.Sql("SET IDENTITY_INSERT [Users] ON");
            migrationBuilder.Sql(
                """
                IF NOT EXISTS (SELECT 1 FROM [Users] WHERE [Id] = 1)
                BEGIN
                    INSERT INTO [Users] ([Id], [Avatar], [CreatedAt], [Email], [FullName], [IsActive], [Password], [RoleId], [Username])
                    VALUES (1, NULL, '2026-03-18T06:36:35.0803815Z', N'admin@zenpace.com', N'Quản trị viên Zenpace', 1, N'AdminPassword123!', 1, N'admin');
                END
                """);
            migrationBuilder.Sql("SET IDENTITY_INSERT [Users] OFF");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM [Categories] WHERE [Id] IN (1, 2, 3);
                DELETE FROM [Users] WHERE [Id] = 1;
                """);
        }
    }
}
