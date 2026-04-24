using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using mAIx.Data;

#nullable disable

namespace mAIx.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(mAIxDbContext))]
    [Migration("20260424000016_FixInternetMessageIdUniqueIndex")]
    public partial class FixInternetMessageIdUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. 기존 UNIQUE 단독 인덱스 삭제
            migrationBuilder.DropIndex(
                name: "IX_Email_InternetMessageId",
                table: "Emails");

            // 2. non-UNIQUE 단독 인덱스 재생성 (검색용)
            migrationBuilder.CreateIndex(
                name: "IX_Email_InternetMessageId",
                table: "Emails",
                column: "InternetMessageId");

            // 3. InternetMessageId + ParentFolderId 복합 UNIQUE 생성
            //    (같은 메일이 보낸편지함/받은편지함에 동시 존재 허용)
            migrationBuilder.CreateIndex(
                name: "IX_Email_InternetMessageId_ParentFolderId",
                table: "Emails",
                columns: new[] { "InternetMessageId", "ParentFolderId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Email_InternetMessageId_ParentFolderId",
                table: "Emails");

            migrationBuilder.DropIndex(
                name: "IX_Email_InternetMessageId",
                table: "Emails");

            // 원복: UNIQUE 단독 인덱스 재생성
            migrationBuilder.CreateIndex(
                name: "IX_Email_InternetMessageId",
                table: "Emails",
                column: "InternetMessageId",
                unique: true);
        }
    }
}
