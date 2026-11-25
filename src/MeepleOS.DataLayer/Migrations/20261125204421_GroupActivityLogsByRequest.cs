using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeepleOS.DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class GroupActivityLogsByRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "ActivityLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_RequestId_AccountId",
                table: "ActivityLogs",
                columns: new[] { "RequestId", "AccountId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ActivityLogs_RequestId_AccountId",
                table: "ActivityLogs");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "ActivityLogs");
        }
    }
}
