using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForumZenpace.Migrations
{
    /// <inheritdoc />
    public partial class AddStoriesFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StoryId",
                table: "Notifications",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Stories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    TextContent = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true),
                    BackgroundStyle = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "aurora"),
                    ImageFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ImageOriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ImageContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ImageUrl = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stories_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoryViews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoryId = table.Column<int>(type: "int", nullable: false),
                    ViewerUserId = table.Column<int>(type: "int", nullable: false),
                    ViewedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoryViews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoryViews_Stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "Stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoryViews_Users_ViewerUserId",
                        column: x => x.ViewerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_StoryId",
                table: "Notifications",
                column: "StoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Stories_ExpiresAt",
                table: "Stories",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Stories_UserId_CreatedAt",
                table: "Stories",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StoryViews_StoryId_ViewerUserId",
                table: "StoryViews",
                columns: new[] { "StoryId", "ViewerUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoryViews_ViewerUserId",
                table: "StoryViews",
                column: "ViewerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Stories_StoryId",
                table: "Notifications",
                column: "StoryId",
                principalTable: "Stories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Stories_StoryId",
                table: "Notifications");

            migrationBuilder.DropTable(
                name: "StoryViews");

            migrationBuilder.DropTable(
                name: "Stories");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_StoryId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "StoryId",
                table: "Notifications");
        }
    }
}
