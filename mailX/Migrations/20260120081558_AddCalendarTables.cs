using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace mailX.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CalendarEvents 테이블 생성
            migrationBuilder.CreateTable(
                name: "CalendarEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GraphId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    ICalUId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    SeriesMasterId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    BodyContentType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Location = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    StartDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartTimeZone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    EndTimeZone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    IsAllDay = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsRecurring = table.Column<bool>(type: "INTEGER", nullable: false),
                    RecurrencePattern = table.Column<string>(type: "TEXT", nullable: true),
                    RecurrenceRange = table.Column<string>(type: "TEXT", nullable: true),
                    ShowAs = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ResponseStatus = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Importance = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Sensitivity = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    IsOnlineMeeting = table.Column<bool>(type: "INTEGER", nullable: false),
                    OnlineMeetingUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    OnlineMeetingProvider = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    ReminderMinutesBeforeStart = table.Column<int>(type: "INTEGER", nullable: false),
                    IsReminderOn = table.Column<bool>(type: "INTEGER", nullable: false),
                    OrganizerEmail = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    OrganizerName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Attendees = table.Column<string>(type: "TEXT", nullable: true),
                    Categories = table.Column<string>(type: "TEXT", nullable: true),
                    CalendarId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    CalendarName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    AccountEmail = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    WebLink = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    LastModifiedDateTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedDateTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SyncedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsLocalOnly = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsCancelled = table.Column<bool>(type: "INTEGER", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    OriginalStartTimeZone = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OriginalEndTimeZone = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CalendarSyncStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AccountEmail = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    CalendarId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    CalendarName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    DeltaLink = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SyncStartDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SyncEndDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSyncAddedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSyncUpdatedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSyncDeletedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    LastErrorAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarSyncStates", x => x.Id);
                });

            // CalendarEvents 인덱스 생성
            migrationBuilder.CreateIndex(
                name: "IX_CalendarEvent_AccountEmail_CalendarId",
                table: "CalendarEvents",
                columns: new[] { "AccountEmail", "CalendarId" });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEvent_AccountEmail_StartDateTime",
                table: "CalendarEvents",
                columns: new[] { "AccountEmail", "StartDateTime" });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEvent_GraphId",
                table: "CalendarEvents",
                column: "GraphId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEvent_ICalUId",
                table: "CalendarEvents",
                column: "ICalUId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEvent_SeriesMasterId",
                table: "CalendarEvents",
                column: "SeriesMasterId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEvent_StartDateTime",
                table: "CalendarEvents",
                column: "StartDateTime");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarSyncState_AccountEmail_CalendarId",
                table: "CalendarSyncStates",
                columns: new[] { "AccountEmail", "CalendarId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalendarEvents");

            migrationBuilder.DropTable(
                name: "CalendarSyncStates");
        }
    }
}
