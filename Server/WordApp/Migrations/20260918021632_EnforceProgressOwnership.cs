using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WordApp.Migrations
{
    /// <inheritdoc />
    public partial class EnforceProgressOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Users_UserId",
                table: "Users",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_GrammarProgresses_Users_UserId",
                table: "GrammarProgresses",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_IdiomProgresses_Users_UserId",
                table: "IdiomProgresses",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WordProgresses_Users_UserId",
                table: "WordProgresses",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GrammarProgresses_Users_UserId",
                table: "GrammarProgresses");

            migrationBuilder.DropForeignKey(
                name: "FK_IdiomProgresses_Users_UserId",
                table: "IdiomProgresses");

            migrationBuilder.DropForeignKey(
                name: "FK_WordProgresses_Users_UserId",
                table: "WordProgresses");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Users_UserId",
                table: "Users");
        }
    }
}
