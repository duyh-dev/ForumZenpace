using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForumZenpace.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendationVectors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreferenceVectorData",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VectorData",
                table: "Posts",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreferenceVectorData",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "VectorData",
                table: "Posts");
        }
    }
}
