using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForumZenpace.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryReplyToMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsStoryReply",
                table: "DirectMessages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "StoryId",
                table: "DirectMessages",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DirectMessages_StoryId",
                table: "DirectMessages",
                column: "StoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_DirectMessages_Stories_StoryId",
                table: "DirectMessages",
                column: "StoryId",
                principalTable: "Stories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DirectMessages_Stories_StoryId",
                table: "DirectMessages");

            migrationBuilder.DropIndex(
                name: "IX_DirectMessages_StoryId",
                table: "DirectMessages");

            migrationBuilder.DropColumn(
                name: "IsStoryReply",
                table: "DirectMessages");

            migrationBuilder.DropColumn(
                name: "StoryId",
                table: "DirectMessages");
        }
    }
}
