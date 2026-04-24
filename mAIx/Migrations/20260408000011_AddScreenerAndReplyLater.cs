using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using mAIx.Data;

#nullable disable

namespace mAIx.Migrations
{
    [DbContext(typeof(mAIxDbContext))]
    [Migration("20260408000011_AddScreenerAndReplyLater")]
    public partial class AddScreenerAndReplyLater : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScreenerEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SenderEmail = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    SenderName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, defaultValue: ""),
                    Action = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "blocked"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreenerEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReplyLaterItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmailId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false, defaultValue: ""),
                    SenderEmail = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false, defaultValue: ""),
                    RemindAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsCompleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplyLaterItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScreenerEntry_SenderEmail",
                table: "ScreenerEntries",
                column: "SenderEmail");

            migrationBuilder.CreateIndex(
                name: "IX_ScreenerEntry_Action",
                table: "ScreenerEntries",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_ReplyLaterItem_IsCompleted",
                table: "ReplyLaterItems",
                column: "IsCompleted");

            migrationBuilder.CreateIndex(
                name: "IX_ReplyLaterItem_RemindAt",
                table: "ReplyLaterItems",
                column: "RemindAt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScreenerEntries");

            migrationBuilder.DropTable(
                name: "ReplyLaterItems");
        }
    }
}
