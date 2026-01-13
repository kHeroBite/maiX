using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace mailX.Migrations
{
    /// <inheritdoc />
    public partial class AddConverterSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConverterSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Extension = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SelectedConverter = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConverterSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConverterSetting_Extension",
                table: "ConverterSettings",
                column: "Extension",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConverterSettings");
        }
    }
}
