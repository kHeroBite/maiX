using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace mAIx.Migrations
{
    public partial class AddSnoozedUntil : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SnoozedUntil",
                table: "Emails",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Email_SnoozedUntil",
                table: "Emails",
                column: "SnoozedUntil");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Email_SnoozedUntil",
                table: "Emails");

            migrationBuilder.DropColumn(
                name: "SnoozedUntil",
                table: "Emails");
        }
    }
}
