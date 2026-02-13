using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaiX.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailPinned : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPinned",
                table: "Emails",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PinnedAt",
                table: "Emails",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPinned",
                table: "Emails");

            migrationBuilder.DropColumn(
                name: "PinnedAt",
                table: "Emails");
        }
    }
}
