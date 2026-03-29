using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace mAIx.Migrations
{
    public partial class AddMailRules : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MailRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ConditionType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ConditionValue = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ActionType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ActionValue = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountEmail = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailRules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MailRule_AccountEmail",
                table: "MailRules",
                column: "AccountEmail");

            migrationBuilder.CreateIndex(
                name: "IX_MailRule_IsEnabled",
                table: "MailRules",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_MailRule_Priority",
                table: "MailRules",
                column: "Priority");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MailRules");
        }
    }
}
