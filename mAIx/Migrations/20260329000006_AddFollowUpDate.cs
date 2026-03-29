using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace mAIx.Migrations
{
    public partial class AddFollowUpDate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FollowUpDate",
                table: "Emails",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FollowUpDate",
                table: "Emails");
        }
    }
}
