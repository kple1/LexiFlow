using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WordApp.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GrammarProgresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    GrammarId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CorrectCount = table.Column<int>(type: "integer", nullable: false),
                    WrongCount = table.Column<int>(type: "integer", nullable: false),
                    LastReviewed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrammarProgresses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Grammars",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Example = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Explanation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Grammars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdiomProgresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IdiomId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CorrectCount = table.Column<int>(type: "integer", nullable: false),
                    WrongCount = table.Column<int>(type: "integer", nullable: false),
                    LastReviewed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdiomProgresses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Idioms",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Example = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Explanation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Idioms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Pw = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WordProgresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WordId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CorrectCount = table.Column<int>(type: "integer", nullable: false),
                    WrongCount = table.Column<int>(type: "integer", nullable: false),
                    LastReviewed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WordProgresses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Words",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    English = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Meaning = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    Example = table.Column<string>(type: "text", nullable: true),
                    NotionCreated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Notion")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Words", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GrammarProgresses_UserId_GrammarId",
                table: "GrammarProgresses",
                columns: new[] { "UserId", "GrammarId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdiomProgresses_UserId_IdiomId",
                table: "IdiomProgresses",
                columns: new[] { "UserId", "IdiomId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WordProgresses_UserId_WordId",
                table: "WordProgresses",
                columns: new[] { "UserId", "WordId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GrammarProgresses");

            migrationBuilder.DropTable(
                name: "Grammars");

            migrationBuilder.DropTable(
                name: "IdiomProgresses");

            migrationBuilder.DropTable(
                name: "Idioms");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "WordProgresses");

            migrationBuilder.DropTable(
                name: "Words");
        }
    }
}
