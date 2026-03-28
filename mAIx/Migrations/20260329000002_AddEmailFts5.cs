using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace mAIx.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailFts5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FTS5 가상 테이블 생성 (Emails 테이블 연동)
            migrationBuilder.Sql(@"
                CREATE VIRTUAL TABLE IF NOT EXISTS EmailsFts USING fts5(
                    Subject,
                    BodyText,
                    SenderName,
                    SenderEmail,
                    content='Emails',
                    content_rowid='Id'
                );
            ");

            // INSERT 트리거 — Emails 행 삽입 시 FTS 자동 인덱싱
            migrationBuilder.Sql(@"
                CREATE TRIGGER IF NOT EXISTS emails_ai AFTER INSERT ON [Emails] BEGIN
                    INSERT INTO EmailsFts(rowid, Subject, BodyText, SenderName, SenderEmail)
                    VALUES (new.Id, new.Subject, new.Body, new.[From], new.[From]);
                END;
            ");

            // DELETE 트리거 — Emails 행 삭제 시 FTS 자동 제거
            migrationBuilder.Sql(@"
                CREATE TRIGGER IF NOT EXISTS emails_ad AFTER DELETE ON [Emails] BEGIN
                    INSERT INTO EmailsFts(EmailsFts, rowid, Subject, BodyText, SenderName, SenderEmail)
                    VALUES ('delete', old.Id, old.Subject, old.Body, old.[From], old.[From]);
                END;
            ");

            // UPDATE 트리거 — Emails 행 수정 시 FTS 자동 갱신
            migrationBuilder.Sql(@"
                CREATE TRIGGER IF NOT EXISTS emails_au AFTER UPDATE ON [Emails] BEGIN
                    INSERT INTO EmailsFts(EmailsFts, rowid, Subject, BodyText, SenderName, SenderEmail)
                    VALUES ('delete', old.Id, old.Subject, old.Body, old.[From], old.[From]);
                    INSERT INTO EmailsFts(rowid, Subject, BodyText, SenderName, SenderEmail)
                    VALUES (new.Id, new.Subject, new.Body, new.[From], new.[From]);
                END;
            ");

            // 초기 인덱싱 — 기존 Emails 데이터 FTS에 삽입
            migrationBuilder.Sql(@"
                INSERT INTO EmailsFts(rowid, Subject, BodyText, SenderName, SenderEmail)
                SELECT Id, Subject, Body, [From], [From]
                FROM [Emails];
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS emails_au;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS emails_ad;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS emails_ai;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS EmailsFts;");
        }
    }
}
