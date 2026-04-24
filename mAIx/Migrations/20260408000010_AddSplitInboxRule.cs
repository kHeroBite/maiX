using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using mAIx.Data;

#nullable disable

namespace mAIx.Migrations
{
    [DbContext(typeof(mAIxDbContext))]
    [Migration("20260408000010_AddSplitInboxRule")]
    public partial class AddSplitInboxRule : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SplitInboxRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TabName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Color = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "#0078D4"),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    MatchersJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SplitInboxRules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SplitInboxRule_IsEnabled",
                table: "SplitInboxRules",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_SplitInboxRule_SortOrder",
                table: "SplitInboxRules",
                column: "SortOrder");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SplitInboxRules");
        }
    }
}
