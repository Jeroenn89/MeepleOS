using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeepleOS.DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class AddGameExpansions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameExpansions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BoardGameId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameExpansions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameExpansions_Boardgames_BoardGameId",
                        column: x => x.BoardGameId,
                        principalTable: "Boardgames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GameSessionExpansions",
                columns: table => new
                {
                    GameSessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    GameExpansionId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSessionExpansions", x => new { x.GameSessionId, x.GameExpansionId });
                    table.ForeignKey(
                        name: "FK_GameSessionExpansions_GameExpansions_GameExpansionId",
                        column: x => x.GameExpansionId,
                        principalTable: "GameExpansions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GameSessionExpansions_GameSessions_GameSessionId",
                        column: x => x.GameSessionId,
                        principalTable: "GameSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameExpansions_BoardGameId_NormalizedName",
                table: "GameExpansions",
                columns: new[] { "BoardGameId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameSessionExpansions_GameExpansionId",
                table: "GameSessionExpansions",
                column: "GameExpansionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameSessionExpansions");

            migrationBuilder.DropTable(
                name: "GameExpansions");
        }
    }
}
