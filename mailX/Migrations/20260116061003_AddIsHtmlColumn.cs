using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace mailX.Migrations
{
    /// <inheritdoc />
    public partial class AddIsHtmlColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsHtml",
                table: "Emails",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsHtml",
                table: "Emails");
        }
    }
}
