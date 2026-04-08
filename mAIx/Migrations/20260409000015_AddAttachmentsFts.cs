using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace mAIx.Migrations
{
    public partial class AddAttachmentsFts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE VIRTUAL TABLE IF NOT EXISTS AttachmentsFts USING fts5(
                    FileName,
                    ContentText,
                    content='Attachments',
                    content_rowid='Id',
                    tokenize='trigram'
                );
            ");
            migrationBuilder.Sql(@"
                CREATE TRIGGER IF NOT EXISTS attachments_ai AFTER INSERT ON [Attachments]
                WHEN new.MarkdownContent IS NOT NULL
                BEGIN
                    INSERT INTO AttachmentsFts(rowid, FileName, ContentText)
                    VALUES (new.Id, new.Name, new.MarkdownContent);
                END;
            ");
            migrationBuilder.Sql(@"
                CREATE TRIGGER IF NOT EXISTS attachments_ad AFTER DELETE ON [Attachments]
                WHEN old.MarkdownContent IS NOT NULL
                BEGIN
                    INSERT INTO AttachmentsFts(AttachmentsFts, rowid, FileName, ContentText)
                    VALUES ('delete', old.Id, old.Name, old.MarkdownContent);
                END;
            ");
            migrationBuilder.Sql(@"
                CREATE TRIGGER IF NOT EXISTS attachments_au AFTER UPDATE ON [Attachments]
                BEGIN
                    INSERT INTO AttachmentsFts(AttachmentsFts, rowid, FileName, ContentText)
                    VALUES ('delete', old.Id, old.Name, COALESCE(old.MarkdownContent, ''));
                    INSERT INTO AttachmentsFts(rowid, FileName, ContentText)
                    VALUES (new.Id, new.Name, COALESCE(new.MarkdownContent, ''));
                END;
            ");
            migrationBuilder.Sql(@"
                INSERT INTO AttachmentsFts(rowid, FileName, ContentText)
                SELECT Id, Name, MarkdownContent FROM [Attachments] WHERE MarkdownContent IS NOT NULL;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS attachments_au;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS attachments_ad;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS attachments_ai;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS AttachmentsFts;");
        }
    }
}
