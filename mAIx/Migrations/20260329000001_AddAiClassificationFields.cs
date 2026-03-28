using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace mAIx.Migrations
{
    /// <inheritdoc />
    public partial class AddAiClassificationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiCategory",
                table: "Emails",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiPriority",
                table: "Emails",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "AiActionRequired",
                table: "Emails",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AiSummaryBrief",
                table: "Emails",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiCategory",
                table: "Emails");

            migrationBuilder.DropColumn(
                name: "AiPriority",
                table: "Emails");

            migrationBuilder.DropColumn(
                name: "AiActionRequired",
                table: "Emails");

            migrationBuilder.DropColumn(
                name: "AiSummaryBrief",
                table: "Emails");
        }
    }
}
