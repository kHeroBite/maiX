# DATABASE.md - 데이터베이스 스키마 문서

## 데이터베이스 개요

### 데이터베이스 정보
- **종류**: SQLite (EF Core)
- **경로**: `%APPDATA%\MaiX\MaiX.db`
- **ORM**: Entity Framework Core
- **DbContext**: `MaiX/Data/MaiXDbContext.cs`
- **전체 테이블 수**: 19개

### 마이그레이션 관리
```bash
# 마이그레이션 생성
dotnet ef migrations add {Name} --project MaiX

# 데이터베이스 업데이트
dotnet ef database update --project MaiX

# SQL 스크립트 생성
dotnet ef migrations script --project MaiX
```

---

## 테이블 목록

| 테이블명 | 설명 | 주요 관계 |
|----------|------|-----------|
| Emails | 이메일 데이터 | → Attachments, Todos, ContractInfo |
| Attachments | 첨부파일 | ← Email |
| Folders | 메일 폴더 | - |
| Accounts | M365 계정 | - |
| Todos | 할일 항목 | ← Email |
| ContractInfos | 계약 정보 | ← Email (1:1) |
| AISettings | AI 제공자 설정 | - |
| SyncStates | 동기화 상태 | - |
| Signatures | 이메일 서명 | - |
| Prompts | AI 프롬프트 | → PromptTestHistories |
| PromptTestHistories | 프롬프트 테스트 이력 | ← Prompt |
| TeamsMessages | Teams 메시지 | - |
| OneNotePages | OneNote 페이지 | - |
| ConverterSettings | 문서 변환기 설정 | - |
| MailRules | 메일 자동 처리 규칙 | - |
| QuickSteps | 퀵스텝 (일괄 액션) | - |
| SplitInboxRules | 분할 받은편지함 규칙 | - |
| ScreenerEntries | 발신자 허용/차단 목록 | - |
| ReplyLaterItems | 나중에 답장 목록 | - |

---

## 1. Emails 테이블

**설명**: Graph API에서 가져온 이메일과 AI 분석 결과를 저장

**DbSet**: `MaiXDbContext.Emails`

**컬럼 상세**:
| 컬럼명 | 타입 | NULL | 키 | 기본값 | 설명 |
|--------|------|------|-----|--------|------|
| Id | int | NO | PK | AUTO | 기본 키 |
| InternetMessageId | string(500) | YES | UK | NULL | RFC 2822 메시지 ID |
| EntryId | string(500) | YES | - | NULL | Exchange Entry ID |
| ConversationId | string(500) | YES | IDX | NULL | 대화 스레드 ID |
| Subject | string(1000) | NO | - | - | 이메일 제목 |
| Body | text | YES | - | NULL | 본문 (HTML/텍스트) |
| From | string(500) | NO | - | - | 발신자 이메일 |
| To | text | YES | - | NULL | 수신자 (JSON 배열) |
| Cc | text | YES | - | NULL | 참조 (JSON 배열) |
| Bcc | text | YES | - | NULL | 숨은참조 (JSON 배열) |
| ReceivedDateTime | datetime | YES | IDX | NULL | 수신 일시 |
| IsRead | bool | NO | - | false | 읽음 여부 |
| Importance | string(20) | YES | - | NULL | 중요도 (low/normal/high) |
| HasAttachments | bool | NO | - | false | 첨부파일 존재 여부 |
| ParentFolderId | string(500) | YES | IDX | NULL | 상위 폴더 ID |
| **AI 분석 필드** |
| SummaryOneline | string(500) | YES | - | NULL | AI 한줄 요약 |
| Summary | text | YES | - | NULL | AI 상세 요약 |
| PriorityScore | int | YES | - | NULL | 우선순위 점수 (0-100) |
| PriorityLevel | string(20) | YES | - | NULL | 우선순위 레벨 |
| UrgencyLevel | string(20) | YES | - | NULL | 긴급도 레벨 |
| Deadline | datetime | YES | - | NULL | AI 추출 마감일 |
| IsNonBusiness | bool | NO | - | false | 비업무 메일 여부 |
| MyPosition | string(10) | YES | - | NULL | 내 역할 (to/cc/bcc) |
| Keywords | text | YES | - | NULL | 키워드 (JSON 배열) |
| AnalysisStatus | string(20) | NO | IDX | "pending" | 분석 상태 |
| AccountEmail | string(500) | NO | IDX | - | 소속 계정 |
| **AI 분류 필드 (Phase 0 — 2026-03-29)** |
| AiCategory | string(50) | YES | - | NULL | AI 자동 분류 (긴급/업무/일반) |
| AiPriority | string(20) | YES | - | NULL | AI 우선순위 (high/medium/low) |
| AiActionRequired | bool | NO | - | false | AI 액션 필요 여부 |
| AiSummaryBrief | string(500) | YES | - | NULL | AI 간략 요약 (1-2줄) |
| **스누즈 필드 (Phase 2 — 2026-03-29)** |
| SnoozedUntil | datetime | YES | IDX | NULL | 스누즈 해제 예정 시각 (UTC). null=스누즈 미설정 |
| **팔로업 필드 (Phase 3 — 2026-03-29)** |
| FollowUpDate | datetime | YES | IDX | NULL | 팔로업 예정 날짜 (UTC). null=팔로업 미설정 |

**인덱스**:
- `IX_Email_InternetMessageId` (non-UNIQUE — 2026-04-24 단독 UNIQUE 폐기)
- `IX_Email_InternetMessageId_ParentFolderId` (UNIQUE 복합 — 2026-04-24 추가, 같은 메일이 보낸편지함/받은편지함에 동시 존재 허용)
- `IX_Email_ConversationId`
- `IX_Email_AccountEmail`
- `IX_Email_ReceivedDateTime`
- `IX_Email_ParentFolderId`
- `IX_Email_AnalysisStatus`
- `IX_Email_SnoozedUntil` (Phase 2 추가)
- `IX_Email_FollowUpDate` (Phase 3 추가)

**관계**:
- **1:N** → Attachments (첨부파일)
- **1:N** → Todos (할일)
- **1:1** → ContractInfo (계약정보)

---

## 2. Attachments 테이블

**설명**: 이메일 첨부파일과 변환 상태 관리

**DbSet**: `MaiXDbContext.Attachments`

**컬럼 상세**:
| 컬럼명 | 타입 | NULL | 키 | 기본값 | 설명 |
|--------|------|------|-----|--------|------|
| Id | int | NO | PK | AUTO | 기본 키 |
| EmailId | int | NO | FK | - | 이메일 FK |
| Name | string(500) | NO | - | - | 파일명 |
| ContentType | string(200) | YES | - | NULL | MIME 타입 |
| Size | long | NO | - | 0 | 파일 크기 (바이트) |
| LocalPath | string(1000) | YES | - | NULL | 로컬 저장 경로 |
| OriginalFile | string(1000) | YES | - | NULL | 원본 파일 경로 |
| MarkdownPath | string(1000) | YES | - | NULL | MD 변환 경로 |
| MarkdownContent | text | YES | - | NULL | MD 변환 내용 |
| ConversionStatus | string(20) | NO | IDX | "pending" | 변환 상태 |
| ConverterUsed | string(50) | YES | - | NULL | 사용된 변환기 |
| ExpiresAt | datetime | YES | - | NULL | 만료 시간 |

**변환 상태 값**:
- `pending`: 변환 대기
- `converting`: 변환 중
- `completed`: 변환 완료
- `failed`: 변환 실패
- `skipped`: 변환 건너뜀

**인덱스**:
- `IX_Attachment_EmailId`
- `IX_Attachment_ConversionStatus`

---

## 3. Folders 테이블

**설명**: Outlook 메일 폴더 정보

**DbSet**: `MaiXDbContext.Folders`

**컬럼 상세**:
| 컬럼명 | 타입 | NULL | 키 | 기본값 | 설명 |
|--------|------|------|-----|--------|------|
| Id | string(500) | NO | PK | - | Graph API 폴더 ID |
| DisplayName | string(200) | NO | - | - | 폴더 표시명 |
| ParentFolderId | string(500) | YES | - | NULL | 상위 폴더 ID |
| TotalItemCount | int | NO | - | 0 | 전체 아이템 수 |
| UnreadItemCount | int | NO | - | 0 | 읽지 않은 수 |
| AccountEmail | string(500) | NO | IDX | - | 소속 계정 |

**인덱스**:
- `IX_Folder_AccountEmail`

---

## 4. Accounts 테이블

**설명**: Microsoft 365 계정 정보 및 인증 토큰

**DbSet**: `MaiXDbContext.Accounts`

**컬럼 상세**:
| 컬럼명 | 타입 | NULL | 키 | 기본값 | 설명 |
|--------|------|------|-----|--------|------|
| Email | string(500) | NO | PK | - | 이메일 주소 |
| DisplayName | string(200) | YES | - | NULL | 표시 이름 |
| Tokens | byte[] | YES | - | NULL | MSAL 토큰 캐시 (암호화) |
| IsDefault | bool | NO | - | false | 기본 계정 여부 |
| LastLoginAt | datetime | YES | - | NULL | 마지막 로그인 |

---

## 5. Todos 테이블

**설명**: AI가 이메일에서 추출한 할 일 항목

**DbSet**: `MaiXDbContext.Todos`

**컬럼 상세**:
| 컬럼명 | 타입 | NULL | 키 | 기본값 | 설명 |
|--------|------|------|-----|--------|------|
| Id | int | NO | PK | AUTO | 기본 키 |
| EmailId | int | NO | FK | - | 이메일 FK |
| Content | string(2000) | NO | - | - | TODO 내용 |
| DueDate | datetime | YES | - | NULL | 마감일 |
| Status | string(20) | NO | - | "pending" | 상태 |
| Priority | int | NO | - | 3 | 우선순위 (1-5) |

**상태 값**:
- `pending`: 대기
- `in_progress`: 진행 중
- `completed`: 완료
- `cancelled`: 취소

---

## 6. ContractInfos 테이블

**설명**: AI가 이메일에서 추출한 계약/사업 관련 정보 (Email과 1:1 관계)

**DbSet**: `MaiXDbContext.ContractInfos`

**컬럼 상세**:
| 컬럼명 | 타입 | NULL | 키 | 기본값 | 설명 |
|--------|------|------|-----|--------|------|
| Id | int | NO | PK | AUTO | 기본 키 |
| EmailId | int | NO | FK,UK | - | 이메일 FK (1:1) |
| Amount | decimal | YES | - | NULL | 계약 금액 (원) |
| Period | string(200) | YES | - | NULL | 계약 기간 |
| ManMonth | decimal | YES | - | NULL | 투입 공수 (M/M) |
| Location | string(500) | YES | - | NULL | 근무 위치 |
| IsRemote | bool | YES | - | NULL | 원격근무 가능 |
| Scope | text | YES | - | NULL | 업무 범위 |
| BusinessType | string(100) | YES | - | NULL | 사업 유형 |

**인덱스**:
- `IX_ContractInfo_EmailId` (UNIQUE)

---

## 7. AISettings 테이블

**설명**: LLM 제공자 및 API 설정

**DbSet**: `MaiXDbContext.AISettings`

**컬럼 상세**:
| 컬럼명 | 타입 | NULL | 키 | 기본값 | 설명 |
|--------|------|------|-----|--------|------|
| Id | int | NO | PK | AUTO | 기본 키 |
| Provider | string(50) | NO | - | - | AI 제공자 |
| ApiKey | string(500) | YES | - | NULL | API 키 (암호화) |
| BaseUrl | string(500) | YES | - | NULL | API 기본 URL |
| Model | string(100) | NO | - | - | 모델 이름 |
| IsDefault | bool | NO | - | false | 기본 설정 |
| IsEnabled | bool | NO | - | true | 활성화 |

**Provider 값**:
- `openai`: OpenAI (GPT)
- `anthropic`: Anthropic (Claude)
- `google`: Google (Gemini)
- `ollama`: Ollama (로컬)
- `lmstudio`: LM Studio (로컬)

---

## 8. SyncStates 테이블

**설명**: Graph API Delta 동기화 상태 관리

**DbSet**: `MaiXDbContext.SyncStates`

**컬럼 상세**:
| 컬럼명 | 타입 | NULL | 키 | 기본값 | 설명 |
|--------|------|------|-----|--------|------|
| Id | int | NO | PK | AUTO | 기본 키 |
| AccountEmail | string(500) | NO | UK | - | 계정 이메일 |
| FolderId | string(500) | YES | UK | NULL | 폴더 ID |
| DeltaLink | text | YES | - | NULL | Delta 링크 |
| LastSyncedAt | datetime | YES | - | NULL | 마지막 동기화 |

**인덱스**:
- `IX_SyncState_AccountEmail_FolderId` (UNIQUE, 복합)

---

## 9. Prompts 테이블

**설명**: AI 프롬프트 템플릿 관리

**DbSet**: `MaiXDbContext.Prompts`

**컬럼 상세**:
| 컬럼명 | 타입 | NULL | 키 | 기본값 | 설명 |
|--------|------|------|-----|--------|------|
| Id | int | NO | PK | AUTO | 기본 키 |
| PromptKey | string(100) | NO | UK | - | 프롬프트 고유 키 |
| Category | string(50) | YES | - | NULL | 카테고리 |
| Name | string(200) | NO | - | - | 표시 이름 |
| Template | text | NO | - | - | 프롬프트 템플릿 |
| Variables | text | YES | - | NULL | 변수 목록 (JSON) |
| IsSystem | bool | NO | - | false | 시스템 프롬프트 |
| IsEnabled | bool | NO | - | true | 활성화 |

**인덱스**:
- `IX_Prompt_PromptKey` (UNIQUE)

---

## 10. ConverterSettings 테이블

**설명**: 문서 변환기 설정 (확장자별 선택된 변환기)

**DbSet**: `MaiXDbContext.ConverterSettings`

**컬럼 상세**:
| 컬럼명 | 타입 | NULL | 키 | 기본값 | 설명 |
|--------|------|------|-----|--------|------|
| Id | int | NO | PK | AUTO | 기본 키 |
| Extension | string(20) | NO | UK | - | 파일 확장자 |
| SelectedConverter | string(100) | NO | - | - | 선택된 변환기 |
| UpdatedAt | datetime | NO | - | NOW | 수정 시간 |
| IsEnabled | bool | NO | - | true | 활성화 |

**Extension 예시**: `.hwp`, `.docx`, `.xlsx`, `.pdf`, `.pptx`

**인덱스**:
- `IX_ConverterSetting_Extension` (UNIQUE)

---

## ER 다이어그램

```
┌─────────────────┐
│    Accounts     │
│ ─────────────── │
│ Email (PK)      │
│ DisplayName     │
│ Tokens          │
│ IsDefault       │
│ LastLoginAt     │
└─────────────────┘
         │
         │ AccountEmail
         ▼
┌─────────────────┐      ┌─────────────────┐
│     Emails      │──────│   Attachments   │
│ ─────────────── │ 1:N  │ ─────────────── │
│ Id (PK)         │      │ Id (PK)         │
│ InternetMsgId   │      │ EmailId (FK)    │
│ Subject         │      │ Name            │
│ Body            │      │ ContentType     │
│ From            │      │ Size            │
│ To              │      │ MarkdownContent │
│ ReceivedDateTime│      │ ConversionStatus│
│ AnalysisStatus  │      └─────────────────┘
│ AccountEmail    │
└─────────────────┘
         │
         │ 1:N / 1:1
         ▼
┌─────────────────┐      ┌─────────────────┐
│      Todos      │      │  ContractInfos  │
│ ─────────────── │      │ ─────────────── │
│ Id (PK)         │      │ Id (PK)         │
│ EmailId (FK)    │      │ EmailId (FK,UK) │
│ Content         │      │ Amount          │
│ DueDate         │      │ Period          │
│ Status          │      │ ManMonth        │
│ Priority        │      │ Location        │
└─────────────────┘      └─────────────────┘

┌─────────────────┐      ┌─────────────────┐
│    Folders      │      │   SyncStates    │
│ ─────────────── │      │ ─────────────── │
│ Id (PK)         │      │ Id (PK)         │
│ DisplayName     │      │ AccountEmail    │
│ ParentFolderId  │      │ FolderId        │
│ TotalItemCount  │      │ DeltaLink       │
│ AccountEmail    │      │ LastSyncedAt    │
└─────────────────┘      └─────────────────┘

┌─────────────────┐      ┌─────────────────┐
│   AISettings    │      │    Prompts      │
│ ─────────────── │      │ ─────────────── │
│ Id (PK)         │      │ Id (PK)         │
│ Provider        │      │ PromptKey (UK)  │
│ ApiKey          │      │ Category        │
│ BaseUrl         │      │ Name            │
│ Model           │      │ Template        │
│ IsDefault       │      │ Variables       │
│ IsEnabled       │      │ IsSystem        │
└─────────────────┘      └─────────────────┘

┌─────────────────┐
│ConverterSettings│
│ ─────────────── │
│ Id (PK)         │
│ Extension (UK)  │
│ SelectedConverter│
│ UpdatedAt       │
│ IsEnabled       │
└─────────────────┘
```

---

## 사용 예시

### EF Core 쿼리 예시

```csharp
// 읽지 않은 이메일 조회
var unreadEmails = await _context.Emails
    .Where(e => !e.IsRead && e.AccountEmail == accountEmail)
    .OrderByDescending(e => e.ReceivedDateTime)
    .ToListAsync();

// 분석 대기 이메일 조회
var pendingEmails = await _context.Emails
    .Where(e => e.AnalysisStatus == "pending")
    .Include(e => e.Attachments)
    .ToListAsync();

// 첨부파일 포함 이메일 조회
var emailWithAttachments = await _context.Emails
    .Where(e => e.Id == emailId)
    .Include(e => e.Attachments)
    .Include(e => e.ContractInfo)
    .Include(e => e.Todos)
    .FirstOrDefaultAsync();
```

---

## 주의사항

### 인코딩
- SQLite는 기본 UTF-8
- 문자열 필드에 한글 저장 정상 지원

### 마이그레이션
- 스키마 변경 시 반드시 마이그레이션 생성
- 프로덕션 배포 전 마이그레이션 스크립트 검토

### 성능
- 대량 데이터 조회 시 `AsNoTracking()` 사용
- 필요한 필드만 `Select()` 로 가져오기
- 인덱스 활용 (AccountEmail, ReceivedDateTime 등)


---

## 15. EmailsFts 가상 테이블 (FTS5 — 전문 검색)

**설명**: Emails 전문 검색(Full-Text Search)용 FTS5 가상 테이블. Migration 20260329000002에서 생성.

**DbSet**: (EF Core DbSet 없음 — Raw SQL 직접 사용)

**컬럼 상세**:
| 컬럼명 | 타입 | 설명 |
|--------|------|------|
| Subject | text | 이메일 제목 |
| Body | text | 본문 |
| [From] | text | 발신자 ([From] 대괄호 필수 — SQLite 예약어) |
| AiSummaryBrief | text | AI 간략 요약 |

**트리거**:
- `EmailsFts_ai`: Emails INSERT 시 EmailsFts에 자동 INSERT
- `EmailsFts_au`: Emails UPDATE 시 EmailsFts에 자동 UPDATE (DELETE + INSERT)
- `EmailsFts_ad`: Emails DELETE 시 EmailsFts에 자동 DELETE

**검색 방법**:
```sql
-- FTS5 MATCH 검색 (우선)
SELECT e.* FROM Emails e
JOIN EmailsFts f ON e.Id = f.rowid
WHERE f.EmailsFts MATCH '검색어'
ORDER BY rank;

-- LIKE 폴백 (FTS5 실패 시)
SELECT * FROM Emails
WHERE Subject LIKE '%검색어%' OR Body LIKE '%검색어%';
```

**SQLite 예약어 주의**:
- `From` 컬럼은 반드시 `[From]`으로 이스케이프 (L-274)
- FTS5 가상 테이블 SQL 작성 시 예약어 확인 필수

**쿼리 파일**: `mAIx/Queries/EmailFtsQueries.cs`

---

## Phase 1 스키마 변경 (2026-03-29)

### Emails 테이블 추가 컬럼
| 컬럼명 | 타입 | NULL | 설명 |
|--------|------|------|------|
| `ScheduledSendTime` | DATETIME | NULL | 예약발송 시간. NULL이면 즉시발송 |

### EmailAnalysisResults 테이블 추가 컬럼
| 컬럼명 | 타입 | NULL | 설명 |
|--------|------|------|------|
| `AttachmentSummary` | TEXT | NULL | 첨부파일 AI 요약 |
| `AttachmentRiskLevel` | TEXT | NULL | 첨부파일 위험 수준 |

### Migration 이력 (Phase 1)
| Migration | 설명 |
|-----------|------|
| `20260329000003_AddScheduledSendTime` | Emails.ScheduledSendTime 컬럼 추가 |

### 예약발송 쿼리 패턴
```csharp
// BackgroundSyncService 예약발송 루프
var pendingEmails = await context.Emails
    .Where(e => e.ScheduledSendTime != null
             && e.ScheduledSendTime <= DateTime.UtcNow
             && !e.IsSent)
    .ToListAsync();
```

---

## Phase 3 스키마 변경 (2026-03-29)

### Emails 테이블 추가 컬럼
| 컬럼명 | 타입 | NULL | 설명 |
|--------|------|------|------|
| `FollowUpDate` | DATETIME | NULL | 팔로업 예정 날짜 (UTC). NULL이면 팔로업 미설정 |

### MailRules 테이블 (신규)

**설명**: 조건 기반 메일 자동 처리 규칙

**DbSet**: `MaiXDbContext.MailRules`

**컬럼 상세**:
| 컬럼명 | 타입 | NULL | 키 | 기본값 | 설명 |
|--------|------|------|-----|--------|------|
| Id | int | NO | PK | AUTO | 기본 키 |
| Name | string(200) | NO | - | - | 규칙 이름 |
| ConditionType | string(50) | NO | - | - | 조건 타입 |
| ConditionValue | string(500) | YES | - | NULL | 조건 값 |
| ActionType | string(50) | NO | - | - | 액션 타입 |
| ActionValue | string(500) | YES | - | NULL | 액션 값 |
| IsEnabled | bool | NO | - | true | 활성화 여부 |
| Priority | int | NO | - | 0 | 실행 우선순위 (낮을수록 먼저) |
| AccountEmail | string(500) | YES | - | NULL | 계정별 규칙 (null=전체) |
| CreatedAt | datetime | NO | - | UtcNow | 생성 일시 |

**ConditionType 값**:
- `FromContains`: 발신자 주소 포함
- `SubjectContains`: 제목 포함
- `HasAttachment`: 첨부파일 존재
- `AiCategoryEquals`: AI 분류 일치
- `ToContains`: 수신자 주소 포함

**ActionType 값**:
- `MoveToFolder`: 폴더 이동 (ActionValue = 폴더명)
- `SetCategory`: 카테고리 설정 (ActionValue = 카테고리명)
- `SetFlag`: 플래그 설정
- `MarkAsRead`: 읽음 처리
- `Delete`: 삭제

### Migration 이력 (Phase 3)
| Migration | 설명 |
|-----------|------|
| `20260329000005_AddMailRules` | MailRules 테이블 생성 |
| `20260329000006_AddFollowUpDate` | Emails.FollowUpDate 컬럼 + IX_Email_FollowUpDate 인덱스 추가 |

### 팔로업 쿼리 패턴
```csharp
// BackgroundSyncService 팔로업 루프 (3600초)
var followUpEmails = await context.Emails
    .Where(e => e.FollowUpDate.HasValue
             && e.FollowUpDate <= DateTime.UtcNow)
    .ToListAsync();
```

### 규칙엔진 쿼리 패턴
```csharp
// MailRuleService — 활성 규칙 우선순위 정렬 로드
var rules = await context.MailRules
    .Where(r => r.IsEnabled && (r.AccountEmail == null || r.AccountEmail == accountEmail))
    .OrderBy(r => r.Priority)
    .ToListAsync();
```

---

## k5 파이프라인 스키마 변경 (2026-04-08)

### Migration 이력 (k5 파이프라인)
| Migration | 설명 |
|-----------|------|
| `20260408000007_AddEmailCompositeIndex` | Emails 복합 인덱스 추가 |
| `20260408000008_AddQuickStep` | QuickSteps 테이블 생성 |
| `20260408000009_AddConversationIndex` | Emails.ConversationId 인덱스 추가 |
| `20260408000010_AddSplitInboxRule` | SplitInboxRules 테이블 생성 |
| `20260408000011_AddScreenerAndReplyLater` | ScreenerEntries, ReplyLaterItems 테이블 생성 |

### Emails 테이블 인덱스 추가 (Migration: 20260408000009)

**인덱스 추가**:
- `IX_Email_ConversationId` — ConversationId 컬럼 인덱스 (대화 스레드 그룹핑 성능 향상)

---

## 16. QuickSteps 테이블

**설명**: 퀵스텝 — 여러 액션을 묶어 한 번에 실행하는 사용자 정의 자동화

**Migration**: `20260408000008_AddQuickStep`

**DbSet**: `MaiXDbContext.QuickSteps`

**컬럼 상세**:
| 컬럼명 | 타입 | NULL | 키 | 기본값 | 설명 |
|--------|------|------|-----|--------|------|
| Id | int | NO | PK | AUTO | 기본 키 |
| Name | string | NO | - | - | 퀵스텝 이름 |
| Actions | string | NO | - | - | 직렬화된 액션 목록 (JSON) |
| Shortcut | string | YES | - | NULL | 키보드 단축키 |
| CreatedAt | datetime | NO | - | UtcNow | 생성 일시 |

---

## 17. SplitInboxRules 테이블

**설명**: 분할 받은편지함 규칙 — 조건에 따라 메일을 여러 탭으로 분류

**Migration**: `20260408000010_AddSplitInboxRule`

**DbSet**: `MaiXDbContext.SplitInboxRules`

**컬럼 상세**:
| 컬럼명 | 타입 | NULL | 키 | 기본값 | 설명 |
|--------|------|------|-----|--------|------|
| Id | int | NO | PK | AUTO | 기본 키 |
| TabName | string | NO | - | - | 탭 이름 |
| Criteria | string | NO | - | - | 필터 조건 (JSON) |
| Priority | int | NO | - | 0 | 우선순위 (낮을수록 먼저) |
| IsEnabled | bool | NO | - | true | 활성화 여부 |

---

## 18. ScreenerEntries 테이블

**설명**: 발신자 허용/차단 목록 — 수신 메일 발신자 필터링

**Migration**: `20260408000011_AddScreenerAndReplyLater`

**DbSet**: `MaiXDbContext.ScreenerEntries`

**컬럼 상세**:
| 컬럼명 | 타입 | NULL | 키 | 기본값 | 설명 |
|--------|------|------|-----|--------|------|
| Id | int | NO | PK | AUTO | 기본 키 |
| EmailAddress | string | NO | UK | - | 발신자 이메일 주소 |
| IsAllowed | bool | NO | - | true | true=허용, false=차단 |
| AddedAt | datetime | NO | - | UtcNow | 추가 일시 |

**인덱스**:
- `IX_ScreenerEntry_EmailAddress` (UNIQUE)

---

## 19. ReplyLaterItems 테이블

**설명**: 나중에 답장 목록 — 메일을 스누즈하여 지정 시각에 다시 알림

**Migration**: `20260408000011_AddScreenerAndReplyLater`

**DbSet**: `MaiXDbContext.ReplyLaterItems`

**컬럼 상세**:
| 컬럼명 | 타입 | NULL | 키 | 기본값 | 설명 |
|--------|------|------|-----|--------|------|
| Id | int | NO | PK | AUTO | 기본 키 |
| EmailMessageId | string | NO | - | - | 메일 식별자 |
| SnoozeUntil | datetime | NO | - | - | 스누즈 해제 시각 (UTC) |
| Subject | string | YES | - | NULL | 메일 제목 |
| CreatedAt | datetime | NO | - | UtcNow | 생성 일시 |

