using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaiX.Migrations
{
    /// <inheritdoc />
    public partial class AddChatFavorites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatFavorites",
                columns: table => new
                {
                    ChatId = table.Column<string>(type: "TEXT", nullable: false),
                    AccountEmail = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    ChatType = table.Column<string>(type: "TEXT", nullable: true),
                    FavoritedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatFavorites", x => x.ChatId);
                });

            // 인덱스 추가
            migrationBuilder.CreateIndex(
                name: "IX_ChatFavorites_AccountEmail",
                table: "ChatFavorites",
                column: "AccountEmail");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatFavorites");
        }
    }
}
