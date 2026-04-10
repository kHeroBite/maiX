using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace mAIx.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamsChannelModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Email_ParentFolderId_ReceivedDateTime",
                table: "Emails");

            migrationBuilder.DropIndex(
                name: "IX_ChatFavorites_AccountEmail",
                table: "ChatFavorites");

            migrationBuilder.CreateTable(
                name: "ChannelNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChannelId = table.Column<string>(type: "TEXT", nullable: false),
                    TeamId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelNotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChannelNotificationSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChannelId = table.Column<string>(type: "TEXT", nullable: false),
                    TeamId = table.Column<string>(type: "TEXT", nullable: false),
                    NotifyOnNewPost = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotifyOnMention = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsMuted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelNotificationSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlannerCustomFields",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TaskId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    FieldName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FieldValue = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    FieldType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannerCustomFields", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelNote_ChannelId_TeamId",
                table: "ChannelNotes",
                columns: new[] { "ChannelId", "TeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelNotificationSetting_ChannelId_TeamId",
                table: "ChannelNotificationSettings",
                columns: new[] { "ChannelId", "TeamId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelNotes");

            migrationBuilder.DropTable(
                name: "ChannelNotificationSettings");

            migrationBuilder.DropTable(
                name: "PlannerCustomFields");

            migrationBuilder.CreateIndex(
                name: "IX_Email_ParentFolderId_ReceivedDateTime",
                table: "Emails",
                columns: new[] { "ParentFolderId", "ReceivedDateTime" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatFavorites_AccountEmail",
                table: "ChatFavorites",
                column: "AccountEmail");
        }
    }
}
