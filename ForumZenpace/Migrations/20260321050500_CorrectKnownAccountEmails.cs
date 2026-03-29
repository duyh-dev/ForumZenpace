using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForumZenpace.Migrations
{
    public partial class CorrectKnownAccountEmails : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE [Users]
                SET [Email] = N'tuantqtb01555@gmail.com',
                    [IsEmailConfirmed] = 0,
                    [EmailVerificationToken] = NULL,
                    [EmailVerificationTokenExpiresAt] = NULL
                WHERE [Username] = N'Tuandeptrai';

                UPDATE [Users]
                SET [Email] = N'adminzenpace@gmail.com',
                    [IsEmailConfirmed] = 0,
                    [EmailVerificationToken] = NULL,
                    [EmailVerificationTokenExpiresAt] = NULL
                WHERE [Username] = N'admin';
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE [Users]
                SET [Email] = N'adminzenpace@gmail.com'
                WHERE [Username] = N'Tuandeptrai';

                UPDATE [Users]
                SET [Email] = N'admin@forumzenpace.com'
                WHERE [Username] = N'admin';
                """);
        }
    }
}
