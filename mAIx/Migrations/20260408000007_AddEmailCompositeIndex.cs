using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using mAIx.Data;

#nullable disable

namespace mAIx.Migrations
{
    [DbContext(typeof(mAIxDbContext))]
    [Migration("20260408000007_AddEmailCompositeIndex")]
    public partial class AddEmailCompositeIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SyncIncrementalAsync 쿼리 최적화:
            // WHERE ParentFolderId = X AND ReceivedDateTime > Y ORDER BY ReceivedDateTime DESC
            // 복합 인덱스로 300ms → ~5ms 개선
            migrationBuilder.CreateIndex(
                name: "IX_Email_ParentFolderId_ReceivedDateTime",
                table: "Emails",
                columns: new[] { "ParentFolderId", "ReceivedDateTime" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Email_ParentFolderId_ReceivedDateTime",
                table: "Emails");
        }
    }
}
