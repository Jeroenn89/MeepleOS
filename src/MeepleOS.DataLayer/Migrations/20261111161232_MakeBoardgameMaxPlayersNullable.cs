using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeepleOS.DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class MakeBoardgameMaxPlayersNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "MaxPlayers",
                table: "Boardgames",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "MaxPlayers",
                table: "Boardgames",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
