using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using mAIx.Data;

#nullable disable

namespace mAIx.Migrations
{
    [DbContext(typeof(mAIxDbContext))]
    [Migration("20260408000008_AddQuickStep")]
    public partial class AddQuickStep : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuickSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false, defaultValue: ""),
                    ActionsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuickSteps", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuickStep_IsEnabled",
                table: "QuickSteps",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_QuickStep_SortOrder",
                table: "QuickSteps",
                column: "SortOrder");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuickSteps");
        }
    }
}
