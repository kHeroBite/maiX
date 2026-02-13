# MCP.md - MCP 서버 통합 문서

MaiX 프로젝트에서 사용하는 MCP(Model Context Protocol) 서버의 통합 가이드입니다.

---

## 목차

1. [개요](#개요)
2. [Serena MCP (코드 분석/편집)](#serena-mcp-코드-분석편집)
3. [MySQL MCP (데이터베이스)](#mysql-mcp-데이터베이스)
4. [Sequential Thinking (문제 분석/설계)](#sequential-thinking-문제-분석설계)
5. [ref MCP (토큰 절약)](#ref-mcp-토큰-절약)
6. [Context7 MCP (전체 문서 배치)](#context7-mcp-전체-문서-배치)
7. [vibe-check MCP (계획 검토)](#vibe-check-mcp-계획-검토)
8. [Outlook MCP (이메일)](#outlook-mcp-이메일)
9. [우선순위 정책](#우선순위-정책)

---

## 개요

### MCP(Model Context Protocol)란?
MCP는 AI 어시스턴트가 외부 데이터 소스나 도구에 접근할 수 있도록 하는 프로토콜입니다.

### MaiX 프로젝트에서 사용하는 MCP 서버

**현재 사용 중** (7개):
1. **Serena MCP**: LSP 기반 코드 분석/편집 (로컬 코드베이스)
2. **MySQL MCP**: 데이터베이스 작업 (UTF-8 인코딩 보장)
3. **Sequential Thinking**: 복잡한 문제 분석 및 작업 관리 (단계별 사고)
4. **ref MCP**: 토큰 절약형 문서 검색 (상태 유지 세션)
5. **Context7 MCP**: 전체 문서 배치 (무료, API 키 불필요)
6. **vibe-check MCP**: 계획 검토 및 터널 비전 방지
7. **Outlook MCP**: Microsoft Outlook 이메일 조작 (로컬)

---

## Serena MCP (코드 분석/편집)

### 개요
Language Server Protocol(LSP) 기반으로 로컬 코드베이스를 분석하고 편집하는 MCP 서버입니다.

### 핵심 특징
- **토큰 효율성**: 전체 파일 읽기 대신 필요한 심볼만 조회
- **의미론적 분석**: LSP 기반으로 정확한 코드 구조 이해
- **무료 오픈소스**: MIT 라이선스, API 키 불필요
- **다국어 지원**: C#, Python, TypeScript, Rust, Go, Java 등

### 트리거 키워드
다음 키워드가 포함된 요청 시 자동 활성화:
- "코드 분석", "구조 파악", "심볼 검색"
- "리팩토링", "이름 변경", "일괄 수정"
- "참조 추적", "호출 위치", "사용처"

### 주요 도구

#### 1. find_symbol
전역/로컬 심볼 빠른 검색

```
find_symbol("MaiXDbContext")
→ Data/MaiXDbContext.cs:8 정확한 위치 반환
```

#### 2. find_referencing_symbols
특정 심볼을 참조하는 모든 위치 추적

```
find_referencing_symbols("GraphMailService")
→ 모든 호출 위치 목록 반환
```

#### 3. get_symbols_overview
파일 내 최상위 심볼 개요 조회

```
get_symbols_overview("MaiX/Data/MaiXDbContext.cs")
→ DbSet, OnModelCreating 등 모든 멤버 목록
```

#### 4. rename_symbol
언어 서버 기반 심볼명 리팩토링

```
rename_symbol("OldMethodName" → "NewMethodName")
→ 전체 프로젝트에서 자동 일괄 변경
```

#### 5. replace_symbol_body
심볼 전체 정의 교체

```
replace_symbol_body("HandleHealth", "새로운 함수 본문")
→ 함수 시그니처 유지하면서 본문만 교체
```

### MaiX 프로젝트 활용 시나리오

#### 시나리오 1: Service 구조 파악
```
get_symbols_overview("MaiX/Services/Graph/GraphMailService.cs")
→ GetEmailsAsync, SendEmailAsync, GetFoldersAsync 등 메서드 목록
```

#### 시나리오 2: ViewModel 리팩토링
```
rename_symbol("LoginViewModel/LoginCommand" → "LoginViewModel/SignInCommand")
→ 전체 프로젝트에서 자동 변경
```

---

## MySQL MCP (데이터베이스)

### 개요
MySQL 데이터베이스 작업을 위한 MCP 서버입니다.

### 주요 도구

#### 1. query
SELECT 쿼리 실행

```sql
mcp__mysql__query("SELECT * FROM Emails WHERE IsRead = 0")
```

#### 2. execute
INSERT/UPDATE/DELETE 쿼리 실행

```sql
mcp__mysql__execute("UPDATE Emails SET IsRead = 1 WHERE Id = 123")
```

#### 3. list_tables
테이블 목록 조회

```
mcp__mysql__list_tables()
```

#### 4. get_schema
테이블 스키마 조회

```
mcp__mysql__get_schema("Emails")
```

### 사용 시 주의사항
- MaiX는 SQLite를 사용하므로 MySQL MCP는 주로 외부 DB 연동 시 사용
- UTF-8 인코딩 주의 (한글 데이터)

---

## Sequential Thinking (문제 분석/설계)

### 개요
복잡한 문제를 단계별로 분석하고 해결책을 도출하는 MCP 서버입니다.

### 트리거 키워드
- "복잡한 문제", "단계별 분석"
- "설계 검토", "아키텍처 결정"
- "리팩토링 계획", "마이그레이션 계획"

### 사용 예시

```
mcp__sequential-thinking__sequentialthinking({
  thought: "MaiX 로그인 실패 원인 분석",
  thoughtNumber: 1,
  totalThoughts: 5,
  nextThoughtNeeded: true
})
```

### 활용 시나리오
1. **버그 분석**: 복잡한 버그의 근본 원인 추적
2. **설계 결정**: 여러 대안 중 최적 방안 선택
3. **리팩토링 계획**: 대규모 코드 변경 계획 수립

---

## ref MCP (토큰 절약)

### 개요
웹 문서 검색 및 읽기를 토큰 효율적으로 수행하는 MCP 서버입니다.

### 주요 도구

#### 1. ref_search_documentation
문서 검색

```
mcp__ref__ref_search_documentation("WPF MVVM CommunityToolkit")
→ 관련 문서 URL 목록 반환
```

#### 2. ref_read_url
URL 내용 읽기

```
mcp__ref__ref_read_url("https://docs.microsoft.com/...")
→ 문서 내용 마크다운으로 반환
```

### 사용 시나리오
- Microsoft Graph API 문서 검색
- WPF UI 라이브러리 사용법 확인
- EF Core 마이그레이션 가이드 참조

---

## Context7 MCP (전체 문서 배치)

### 개요
라이브러리 문서를 전체 배치로 가져오는 MCP 서버입니다 (무료, API 키 불필요).

### 주요 도구

#### 1. resolve-library-id
라이브러리 ID 검색

```
mcp__context7__resolve-library-id({
  libraryName: "Microsoft.Graph",
  query: "How to get emails"
})
```

#### 2. query-docs
문서 쿼리

```
mcp__context7__query-docs({
  libraryId: "/microsoft/graph",
  query: "Get user's mail messages"
})
```

### 사용 시나리오
- 새 라이브러리 도입 시 전체 문서 파악
- API 사용법 예제 검색
- Breaking Changes 확인

---

## vibe-check MCP (계획 검토)

### 개요
계획을 검토하고 터널 비전을 방지하는 MCP 서버입니다.

### 주요 도구

#### 1. vibe_check
계획 검토 및 메타인지 질문 생성

```
mcp__vibe-check-mcp__vibe_check({
  goal: "MaiX 로그인 버그 수정",
  plan: "1. 로그 분석 2. ShutdownMode 수정 3. 테스트",
  progress: "로그 분석 완료"
})
```

#### 2. vibe_learn
실수 및 패턴 학습

```
mcp__vibe-check-mcp__vibe_learn({
  mistake: "WPF ShutdownMode 기본값 확인 안함",
  category: "Premature Implementation",
  type: "mistake",
  solution: "WPF 문서에서 기본값 확인 후 구현"
})
```

### 활용 시나리오
1. **계획 검토**: 구현 전 계획 검토 및 개선점 도출
2. **실수 학습**: 반복되는 실수 패턴 인식 및 방지
3. **터널 비전 방지**: 다른 관점에서 문제 바라보기

---

## Outlook MCP (이메일)

### 개요
로컬 Microsoft Outlook과 직접 통신하여 이메일을 조작하는 MCP 서버입니다.

### 주요 도구

#### 1. get_emails
이메일 목록 조회

```
mcp__outlook-local__get_emails({
  folder: "inbox",
  count: 10,
  unread_only: true
})
```

#### 2. read_email
이메일 상세 읽기

```
mcp__outlook-local__read_email({
  message_id: "EntryID"
})
```

#### 3. send_email
이메일 전송

```
mcp__outlook-local__send_email({
  to: "recipient@example.com",
  subject: "테스트",
  body: "본문 내용"
})
```

#### 4. search_emails
이메일 검색

```
mcp__outlook-local__search_emails({
  query: "프로젝트 계약",
  folder: "inbox",
  count: 20
})
```

#### 5. list_folders
폴더 목록 조회

```
mcp__outlook-local__list_folders({
  include_subfolders: true
})
```

### 사용 시나리오
- MaiX 개발 중 실제 Outlook 데이터 확인
- 테스트용 이메일 데이터 수집
- 이메일 구조 분석

---

## 우선순위 정책

### 문서 검색 우선순위

```
1순위: Context7 MCP (전체 배치, 무료)
   → 새 라이브러리, 전체 문서 필요 시

2순위: ref MCP (토큰 절약)
   → 특정 문서 세션, 반복 조회 시

3순위: WebFetch (기본)
   → 단순 URL 읽기
```

### 코드 분석 우선순위

```
1순위: Serena MCP (심볼 분석)
   → 함수/클래스 검색, 리팩토링

2순위: Glob/Grep (패턴 검색)
   → 파일명/텍스트 검색

3순위: Read (전체 파일)
   → 특정 파일 전체 확인
```

### 문제 해결 우선순위

```
1순위: Sequential Thinking (복잡한 문제)
   → 다단계 분석, 설계 결정

2순위: vibe-check (계획 검토)
   → 구현 전 검토, 터널 비전 방지

3순위: 직접 분석
   → 단순 문제, 즉시 해결 가능
```

---

## 설정 파일 위치

MCP 서버 설정은 다음 파일에서 관리됩니다:
- **Claude Desktop**: `%APPDATA%\Claude\claude_desktop_config.json`
- **Claude Code**: 프로젝트별 `.mcp.json` 또는 `~/.config/claude-code/mcp.json`

### 설정 예시

```json
{
  "mcpServers": {
    "serena": {
      "command": "uvx",
      "args": ["--from", "serena-mcp", "serena"]
    },
    "mysql": {
      "command": "node",
      "args": ["path/to/mysql-mcp/index.js"],
      "env": {
        "MYSQL_HOST": "localhost",
        "MYSQL_DATABASE": "mailx"
      }
    },
    "context7": {
      "command": "npx",
      "args": ["-y", "@context7/mcp"]
    },
    "sequential-thinking": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-sequential-thinking"]
    }
  }
}
```

---

## 문제 해결

### Serena MCP 연결 실패
```bash
# uv 재설치
pip install uv
uvx --from serena-mcp serena
```

### MySQL MCP 인코딩 문제
```sql
-- 연결 시 UTF-8 설정
SET NAMES utf8mb4;
```

### Context7 타임아웃
```
# 네트워크 확인 후 재시도
# 또는 ref MCP로 대체
```
