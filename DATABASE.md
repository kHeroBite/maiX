# DATABASE.md - 데이터베이스 스키마 문서

## 데이터베이스 개요

### 데이터베이스 정보
- **종류**: SQLite (EF Core)
- **경로**: `%APPDATA%\MaiX\MaiX.db`
- **ORM**: Entity Framework Core
- **DbContext**: `MaiX/Data/MaiXDbContext.cs`
- **전체 테이블 수**: 14개

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

**인덱스**:
- `IX_Email_InternetMessageId` (UNIQUE)
- `IX_Email_ConversationId`
- `IX_Email_AccountEmail`
- `IX_Email_ReceivedDateTime`
- `IX_Email_ParentFolderId`
- `IX_Email_AnalysisStatus`

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
