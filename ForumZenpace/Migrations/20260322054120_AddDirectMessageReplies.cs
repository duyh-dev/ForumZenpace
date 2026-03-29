using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForumZenpace.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectMessageReplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReplyToMessageId",
                table: "DirectMessages",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DirectMessages_ReplyToMessageId",
                table: "DirectMessages",
                column: "ReplyToMessageId");

            migrationBuilder.AddForeignKey(
                name: "FK_DirectMessages_DirectMessages_ReplyToMessageId",
                table: "DirectMessages",
                column: "ReplyToMessageId",
                principalTable: "DirectMessages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DirectMessages_DirectMessages_ReplyToMessageId",
                table: "DirectMessages");

            migrationBuilder.DropIndex(
                name: "IX_DirectMessages_ReplyToMessageId",
                table: "DirectMessages");

            migrationBuilder.DropColumn(
                name: "ReplyToMessageId",
                table: "DirectMessages");
        }
    }
}
