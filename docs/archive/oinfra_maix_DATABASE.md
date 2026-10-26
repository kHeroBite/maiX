> 보관: 2026-10-26, 구 oinfra_maix 스킬 부속 문서. 고유 내용은 루트 DATABASE.md 20절·Migration 목록·EF Core 추가 주의사항으로 병합됨.
# DATABASE.md — MaiX 프로젝트 DB 스키마

> SQLite + EF Core. 마이그레이션 이력 및 테이블 스키마 문서.

---

## 마이그레이션 이력 (최신순)

| Migration | 설명 |
|-----------|------|
| 20260409000013_AddPlannerCustomField | PlannerCustomFields 테이블 추가 |
| 20260409000012_AddCalendarSyncToken | CalendarSyncTokens 테이블 추가 |
| 20260408000011_AddScreenerAndReplyLater | ScreenerEntries, ReplyLaterItems 테이블 추가 |
| 20260408000010_AddSplitInboxRule | SplitInboxRules 테이블 추가 |
| 20260408000009_AddConversationIndex | Emails.ConversationId 인덱스 추가 |
| 20260408000008_AddQuickStep | QuickSteps 테이블 추가 |
| 20260408000007_AddEmailCompositeIndex | Emails 복합 인덱스 추가 |

---

## 테이블 스키마

### PlannerCustomFields (20260409000013)

```sql
CREATE TABLE PlannerCustomFields (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    TaskId      TEXT    NOT NULL,
    FieldName   TEXT    NOT NULL,
    FieldValue  TEXT,
    CreatedAt   TEXT    NOT NULL  -- UTC ISO8601
);
```

- **용도**: Planner 태스크의 사용자 정의 필드 저장
- **Model**: `PlannerCustomField` (`/mnt/c/DATA/Project/mAIx/mAIx/Models/PlannerCustomField.cs`)

### CalendarSyncTokens (20260409000012)

```sql
CREATE TABLE CalendarSyncTokens (
    CalendarId  TEXT PRIMARY KEY,
    SyncToken   TEXT,
    LastSync    TEXT,   -- UTC ISO8601
    NextLink    TEXT
);
```

- **용도**: Graph API 증분 동기화를 위한 deltaToken/nextLink 저장
- **관련**: GraphActivityService, GraphTeamsService 등에서 증분 sync 시 참조

### ScreenerEntries (20260408000011)

```sql
CREATE TABLE ScreenerEntries (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    SenderEmail TEXT    NOT NULL,
    Action      TEXT    NOT NULL,  -- Allow / Block / Review
    CreatedAt   TEXT    NOT NULL
);
```

### ReplyLaterItems (20260408000011)

```sql
CREATE TABLE ReplyLaterItems (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    MessageId   TEXT    NOT NULL,
    RemindAt    TEXT    NOT NULL,  -- UTC ISO8601
    Note        TEXT,
    CreatedAt   TEXT    NOT NULL
);
```

### SplitInboxRules (20260408000010)

```sql
CREATE TABLE SplitInboxRules (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    RuleName    TEXT    NOT NULL,
    Condition   TEXT    NOT NULL,  -- JSON 직렬화
    TargetTab   TEXT    NOT NULL,
    Priority    INTEGER NOT NULL DEFAULT 0,
    IsEnabled   INTEGER NOT NULL DEFAULT 1
);
```

### QuickSteps (20260408000008)

```sql
CREATE TABLE QuickSteps (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Name        TEXT    NOT NULL,
    Actions     TEXT    NOT NULL,  -- JSON 직렬화
    Shortcut    TEXT,
    CreatedAt   TEXT    NOT NULL
);
```

---

## EF Core 주의사항

- **UNIQUE 위반**: 배치 SaveChangesAsync 실패 시 DbContext 오염 → 개별 저장 + `Entry.State = Detached` 패턴 필수 (L-286)
- **DateTime 저장**: 항상 UTC 기준 저장, 표시 시 로컬 변환
- **FTS5 예약어**: `From`, `To`, `Order`, `Select` 등 예약어 컬럼은 `[컬럼명]` 대괄호 이스케이프 필수 (L-274)
