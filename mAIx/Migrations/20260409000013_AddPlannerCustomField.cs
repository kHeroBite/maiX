using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using mAIx.Data;

#nullable disable

namespace mAIx.Migrations
{
    [DbContext(typeof(mAIxDbContext))]
    [Migration("20260409000013_AddPlannerCustomField")]
    public partial class AddPlannerCustomField : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlannerCustomFields",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TaskId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    FieldName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FieldValue = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false, defaultValue: ""),
                    FieldType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "text")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannerCustomFields", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlannerCustomField_TaskId",
                table: "PlannerCustomFields",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_PlannerCustomField_FieldName",
                table: "PlannerCustomFields",
                column: "FieldName");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlannerCustomFields");
        }
    }
}
