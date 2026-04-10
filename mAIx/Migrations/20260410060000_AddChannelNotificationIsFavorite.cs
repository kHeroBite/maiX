using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace mAIx.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelNotificationIsFavorite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFavorite",
                table: "ChannelNotificationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFavorite",
                table: "ChannelNotificationSettings");
        }
    }
}
