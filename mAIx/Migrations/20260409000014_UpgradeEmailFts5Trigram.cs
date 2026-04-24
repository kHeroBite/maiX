using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using mAIx.Data;

#nullable disable

namespace mAIx.Migrations
{
    [DbContext(typeof(mAIxDbContext))]
    [Migration("20260409000014_UpgradeEmailFts5Trigram")]
    public partial class UpgradeEmailFts5Trigram : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS emails_au;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS emails_ad;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS emails_ai;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS EmailsFts;");
            migrationBuilder.Sql(@"
                CREATE VIRTUAL TABLE IF NOT EXISTS EmailsFts USING fts5(
                    Subject,
                    BodyText,
                    SenderName,
                    SenderEmail,
                    content='Emails',
                    content_rowid='Id',
                    tokenize='trigram'
                );
            ");
            migrationBuilder.Sql(@"
                CREATE TRIGGER IF NOT EXISTS emails_ai AFTER INSERT ON [Emails] BEGIN
                    INSERT INTO EmailsFts(rowid, Subject, BodyText, SenderName, SenderEmail)
                    VALUES (new.Id, new.Subject, new.BodyText, new.SenderName, new.SenderEmail);
                END;
            ");
            migrationBuilder.Sql(@"
                CREATE TRIGGER IF NOT EXISTS emails_ad AFTER DELETE ON [Emails] BEGIN
                    INSERT INTO EmailsFts(EmailsFts, rowid, Subject, BodyText, SenderName, SenderEmail)
                    VALUES ('delete', old.Id, old.Subject, old.BodyText, old.SenderName, old.SenderEmail);
                END;
            ");
            migrationBuilder.Sql(@"
                CREATE TRIGGER IF NOT EXISTS emails_au AFTER UPDATE ON [Emails] BEGIN
                    INSERT INTO EmailsFts(EmailsFts, rowid, Subject, BodyText, SenderName, SenderEmail)
                    VALUES ('delete', old.Id, old.Subject, old.BodyText, old.SenderName, old.SenderEmail);
                    INSERT INTO EmailsFts(rowid, Subject, BodyText, SenderName, SenderEmail)
                    VALUES (new.Id, new.Subject, new.BodyText, new.SenderName, new.SenderEmail);
                END;
            ");
            migrationBuilder.Sql(@"
                INSERT INTO EmailsFts(rowid, Subject, BodyText, SenderName, SenderEmail)
                SELECT Id, Subject, BodyText, SenderName, SenderEmail FROM [Emails];
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS emails_au;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS emails_ad;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS emails_ai;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS EmailsFts;");
            migrationBuilder.Sql(@"
                CREATE VIRTUAL TABLE IF NOT EXISTS EmailsFts USING fts5(
                    Subject,
                    BodyText,
                    SenderName,
                    SenderEmail,
                    content='Emails',
                    content_rowid='Id',
                    tokenize='unicode61'
                );
            ");
            migrationBuilder.Sql(@"
                CREATE TRIGGER IF NOT EXISTS emails_ai AFTER INSERT ON [Emails] BEGIN
                    INSERT INTO EmailsFts(rowid, Subject, BodyText, SenderName, SenderEmail)
                    VALUES (new.Id, new.Subject, new.BodyText, new.SenderName, new.SenderEmail);
                END;
            ");
            migrationBuilder.Sql(@"
                CREATE TRIGGER IF NOT EXISTS emails_ad AFTER DELETE ON [Emails] BEGIN
                    INSERT INTO EmailsFts(EmailsFts, rowid, Subject, BodyText, SenderName, SenderEmail)
                    VALUES ('delete', old.Id, old.Subject, old.BodyText, old.SenderName, old.SenderEmail);
                END;
            ");
            migrationBuilder.Sql(@"
                CREATE TRIGGER IF NOT EXISTS emails_au AFTER UPDATE ON [Emails] BEGIN
                    INSERT INTO EmailsFts(EmailsFts, rowid, Subject, BodyText, SenderName, SenderEmail)
                    VALUES ('delete', old.Id, old.Subject, old.BodyText, old.SenderName, old.SenderEmail);
                    INSERT INTO EmailsFts(rowid, Subject, BodyText, SenderName, SenderEmail)
                    VALUES (new.Id, new.Subject, new.BodyText, new.SenderName, new.SenderEmail);
                END;
            ");
            migrationBuilder.Sql(@"
                INSERT INTO EmailsFts(rowid, Subject, BodyText, SenderName, SenderEmail)
                SELECT Id, Subject, BodyText, SenderName, SenderEmail FROM [Emails];
            ");
        }
    }
}
