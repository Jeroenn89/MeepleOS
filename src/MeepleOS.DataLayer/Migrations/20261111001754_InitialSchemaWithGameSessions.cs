using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeepleOS.DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchemaWithGameSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // De vorige versie gebruikte EnsureCreated en heeft geen migratiehistorie.
            // IF NOT EXISTS behoudt de bestaande bordspellen en spelers bij de eerste upgrade.
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "Boardgames" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Boardgames" PRIMARY KEY AUTOINCREMENT,
                    "Name" TEXT NOT NULL,
                    "MinPlayers" INTEGER NOT NULL,
                    "MaxPlayers" INTEGER NOT NULL,
                    "EstimatedMinutes" INTEGER NOT NULL
                );
                """);
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "Players" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Players" PRIMARY KEY AUTOINCREMENT,
                    "Name" TEXT NOT NULL,
                    "NormalizedName" TEXT NOT NULL
                );
                """);

            migrationBuilder.CreateTable(
                name: "GameSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BoardGameId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayedAt = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameSessions_Boardgames_BoardGameId",
                        column: x => x.BoardGameId,
                        principalTable: "Boardgames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionPlayers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameSessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<int>(type: "INTEGER", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: true),
                    Position = table.Column<int>(type: "INTEGER", nullable: true),
                    IsWinner = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionPlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionPlayers_GameSessions_GameSessionId",
                        column: x => x.GameSessionId,
                        principalTable: "GameSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SessionPlayers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameSessions_BoardGameId_PlayedAt",
                table: "GameSessions",
                columns: new[] { "BoardGameId", "PlayedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GameSessions_PlayedAt",
                table: "GameSessions",
                column: "PlayedAt");

            migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Players_NormalizedName\" ON \"Players\" (\"NormalizedName\");");

            migrationBuilder.CreateIndex(
                name: "IX_SessionPlayers_GameSessionId_PlayerId",
                table: "SessionPlayers",
                columns: new[] { "GameSessionId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionPlayers_PlayerId",
                table: "SessionPlayers",
                column: "PlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionPlayers");

            migrationBuilder.DropTable(
                name: "GameSessions");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "Boardgames");
        }
    }
}
