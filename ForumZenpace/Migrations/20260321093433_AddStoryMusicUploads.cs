using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForumZenpace.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryMusicUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MusicContentType",
                table: "Stories",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MusicFileName",
                table: "Stories",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MusicOriginalFileName",
                table: "Stories",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MusicUrl",
                table: "Stories",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MusicContentType",
                table: "Stories");

            migrationBuilder.DropColumn(
                name: "MusicFileName",
                table: "Stories");

            migrationBuilder.DropColumn(
                name: "MusicOriginalFileName",
                table: "Stories");

            migrationBuilder.DropColumn(
                name: "MusicUrl",
                table: "Stories");
        }
    }
}
