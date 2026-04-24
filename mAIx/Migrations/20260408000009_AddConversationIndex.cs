using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using mAIx.Data;

#nullable disable

namespace mAIx.Migrations
{
    [DbContext(typeof(mAIxDbContext))]
    [Migration("20260408000009_AddConversationIndex")]
    public partial class AddConversationIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IX_Email_ConversationId 인덱스가 이미 존재하므로 노-옵 마이그레이션
            // (Email.ConversationId 컬럼 및 인덱스는 초기 마이그레이션에서 생성됨)
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
