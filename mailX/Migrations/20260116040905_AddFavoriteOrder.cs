using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace mailX.Migrations
{
    /// <inheritdoc />
    public partial class AddFavoriteOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FavoriteOrder",
                table: "Folders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FavoriteOrder",
                table: "Folders");
        }
    }
}
