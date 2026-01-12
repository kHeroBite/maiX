using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace mailX.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Email = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Tokens = table.Column<byte[]>(type: "BLOB", nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Email);
                });

            migrationBuilder.CreateTable(
                name: "AISettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AISettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Emails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InternetMessageId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    EntryId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ConversationId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    From = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    To = table.Column<string>(type: "TEXT", nullable: true),
                    Cc = table.Column<string>(type: "TEXT", nullable: true),
                    Bcc = table.Column<string>(type: "TEXT", nullable: true),
                    ReceivedDateTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsRead = table.Column<bool>(type: "INTEGER", nullable: false),
                    Importance = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    HasAttachments = table.Column<bool>(type: "INTEGER", nullable: false),
                    ParentFolderId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SummaryOneline = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Summary = table.Column<string>(type: "TEXT", nullable: true),
                    PriorityScore = table.Column<int>(type: "INTEGER", nullable: true),
                    PriorityLevel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    UrgencyLevel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Deadline = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsNonBusiness = table.Column<bool>(type: "INTEGER", nullable: false),
                    MyPosition = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Keywords = table.Column<string>(type: "TEXT", nullable: true),
                    AnalysisStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AccountEmail = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Emails", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Folders",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ParentFolderId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    TotalItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UnreadItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountEmail = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Folders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OneNotePages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SectionId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ContentUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    LinkedEmailId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedDateTime = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OneNotePages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Prompts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PromptKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Template = table.Column<string>(type: "TEXT", nullable: false),
                    Variables = table.Column<string>(type: "TEXT", nullable: true),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prompts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Signatures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    HtmlContent = table.Column<string>(type: "TEXT", nullable: true),
                    PlainTextContent = table.Column<string>(type: "TEXT", nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccountEmail = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Signatures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AccountEmail = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    FolderId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DeltaLink = table.Column<string>(type: "TEXT", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeamsMessages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ChatId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Content = table.Column<string>(type: "TEXT", nullable: true),
                    FromUser = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    LinkedEmailId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedDateTime = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamsMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Attachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmailId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    LocalPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    OriginalFile = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    MarkdownPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    MarkdownContent = table.Column<string>(type: "TEXT", nullable: true),
                    ConversionStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ConverterUsed = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Attachments_Emails_EmailId",
                        column: x => x.EmailId,
                        principalTable: "Emails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContractInfos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmailId = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: true),
                    Period = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ManMonth = table.Column<decimal>(type: "TEXT", nullable: true),
                    Location = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsRemote = table.Column<bool>(type: "INTEGER", nullable: true),
                    Scope = table.Column<string>(type: "TEXT", nullable: true),
                    BusinessType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractInfos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractInfos_Emails_EmailId",
                        column: x => x.EmailId,
                        principalTable: "Emails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Todos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmailId = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    DueDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Todos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Todos_Emails_EmailId",
                        column: x => x.EmailId,
                        principalTable: "Emails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromptTestHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PromptId = table.Column<int>(type: "INTEGER", nullable: false),
                    InputData = table.Column<string>(type: "TEXT", nullable: true),
                    OutputResult = table.Column<string>(type: "TEXT", nullable: true),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Tokens = table.Column<int>(type: "INTEGER", nullable: true),
                    ExecutionTime = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptTestHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromptTestHistories_Prompts_PromptId",
                        column: x => x.PromptId,
                        principalTable: "Prompts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attachment_ConversionStatus",
                table: "Attachments",
                column: "ConversionStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Attachment_EmailId",
                table: "Attachments",
                column: "EmailId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractInfo_EmailId",
                table: "ContractInfos",
                column: "EmailId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Email_AccountEmail",
                table: "Emails",
                column: "AccountEmail");

            migrationBuilder.CreateIndex(
                name: "IX_Email_AnalysisStatus",
                table: "Emails",
                column: "AnalysisStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Email_ConversationId",
                table: "Emails",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_Email_InternetMessageId",
                table: "Emails",
                column: "InternetMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Email_ParentFolderId",
                table: "Emails",
                column: "ParentFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_Email_ReceivedDateTime",
                table: "Emails",
                column: "ReceivedDateTime");

            migrationBuilder.CreateIndex(
                name: "IX_Folder_AccountEmail",
                table: "Folders",
                column: "AccountEmail");

            migrationBuilder.CreateIndex(
                name: "IX_Prompt_PromptKey",
                table: "Prompts",
                column: "PromptKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromptTestHistories_PromptId",
                table: "PromptTestHistories",
                column: "PromptId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncState_AccountEmail_FolderId",
                table: "SyncStates",
                columns: new[] { "AccountEmail", "FolderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Todos_EmailId",
                table: "Todos",
                column: "EmailId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "AISettings");

            migrationBuilder.DropTable(
                name: "Attachments");

            migrationBuilder.DropTable(
                name: "ContractInfos");

            migrationBuilder.DropTable(
                name: "Folders");

            migrationBuilder.DropTable(
                name: "OneNotePages");

            migrationBuilder.DropTable(
                name: "PromptTestHistories");

            migrationBuilder.DropTable(
                name: "Signatures");

            migrationBuilder.DropTable(
                name: "SyncStates");

            migrationBuilder.DropTable(
                name: "TeamsMessages");

            migrationBuilder.DropTable(
                name: "Todos");

            migrationBuilder.DropTable(
                name: "Prompts");

            migrationBuilder.DropTable(
                name: "Emails");
        }
    }
}
