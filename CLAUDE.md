# CLAUDE.md - AI Agent 프로젝트 매뉴얼

> **역할**: Claude Code가 mailX 프로젝트에서 작업할 때 필수로 참조해야 하는 핵심 지침서

---

## 필수 규칙 (Critical Rules)

### Bash 명령어 단일 실행 원칙 (최우선)
```yaml
금지:
  - "&&" 연산자 사용 금지
  - "||" 연산자 사용 금지
  - ";" 연산자 사용 금지
  - "|" 파이프 연산자 사용 금지 (예외: grep 필터링)

필수:
  - 각 명령어를 별도의 Bash 호출로 분리
  - 한 번에 하나의 명령어만 실행
```

### 앱 종료 원칙 (필수)
```yaml
필수:
  - 항상 REST API로 종료: curl -X POST http://localhost:5858/api/shutdown -H "Content-Type: application/json" -d '{"force":false}'
  - taskkill, Stop-Process 등 강제 종료 명령 사용 금지

이유:
  - REST API 종료는 앱의 정상적인 종료 프로세스를 보장
  - 데이터 저장, 리소스 정리 등이 올바르게 수행됨
  - 강제 종료는 데이터 손실 위험이 있음
```

---

## 언어 정책 (Language Policy)

**필수 규칙**: 모든 대화는 한국어로 진행

```yaml
Claude_Code_대화:
  기본_언어: 한국어 (Korean)
  Context_요약_후: 한국어 유지 필수
  새_세션_시작: 한국어로 시작

코드_작성:
  주석: 한국어
  변수명: 영어 (C# 표준)
  함수명: 영어 (C# 표준)
  클래스명: 영어 (C# 표준)

문서_작성:
  README: 한국어
  기술문서: 한국어
  Git_커밋: 한국어

예외_허용:
  - 기술 용어: 영어 허용 (REST API, MCP, WPF 등)
  - 코드 예시: 영어 주석 허용
  - 라이브러리명: 영어 그대로 사용
```

---

## 공통 명령어 (자주 사용)

```yaml
빌드: dotnet build "C:\DATA\Project\mailX\mailX\mailX.csproj"

실행: start "" "C:/DATA/Project/mailX/mailX/bin/Debug/net10.0-windows/mailX.exe"
# 주의: 반드시 start 명령어 사용! 다른 방식은 Permission denied 에러 발생

프로세스_확인: tasklist | grep -i mailX

종료: curl -X POST http://localhost:5858/api/shutdown -H "Content-Type: application/json" -d '{"force":false}'

로그_삭제: rm -f "$APPDATA/mailX/logs"/*.log

로그_확인: Read $APPDATA/mailX/logs/YYYYMMDD.log

ntfy_알림: |
  echo '{"topic":"mailX","title":"제목","message":"내용"}' > /tmp/ntfy.json
  curl -X POST https://ntfy.sh -H "Content-Type: application/json" --data-binary @/tmp/ntfy.json
  rm /tmp/ntfy.json

Git_작업:
  필수: commit 시 항상 push까지 함께 실행
  1. git add .
  2. git commit -m "이모지 타입: 커밋 메시지" && git push

Git_커밋_이모지:
  - "✨ feat:" 새 기능
  - "🐛 fix:" 버그 수정
  - "📝 docs:" 문서
  - "♻️ refactor:" 리팩토링
  - "🎨 style:" 코드 스타일
  - "✅ test:" 테스트
  - "🔧 chore:" 기타 작업
  - "🚀 deploy:" 배포
  - "🗑️ remove:" 삭제

스크린샷: curl -X POST http://localhost:5858/api/screenshot

헬스체크: curl -s http://localhost:5858/api/health

최신_로그: curl -s "http://localhost:5858/api/logs/latest?lines=100"
```

---

## 프로젝트 정보

```yaml
프로젝트_이름: mailX
프로젝트_타입: WPF 애플리케이션 (.NET 10)
프로젝트_루트: C:\DATA\Project\mailX
솔루션_파일: C:\DATA\Project\mailX\mailX\mailX.csproj
실행_파일: C:\DATA\Project\mailX\mailX\bin\Debug\net10.0-windows\mailX.exe

경로:
  로그: %APPDATA%\mailX\logs\
  스크린샷: %APPDATA%\mailX\screenshots\
  데이터베이스: %APPDATA%\mailX\mailX.db
  설정: %APPDATA%\mailX\appsettings.json

REST_API:
  포트: 5858
  Base_URL: http://localhost:5858
```

---

## 기본 작업 프로세스

### 완전자동화 필수 원칙
- **모든 작업은 완전자동화로 진행** (사용자 확인 없이 처음부터 끝까지 100% 자동 완료)
- **테스트 통과 필수**: 3단계 테스트 모두 통과해야만 작업 완료
- **테스트 실패 시**: 1단계 계획부터 다시 시작 → 전체 사이클 반복

### 3단계 테스트 필수 통과 정책

**절대 원칙**: 다음 3가지 테스트를 **순서대로 모두 통과**해야만 작업 완료 인정

#### 1단계: 로그 분석 (필수)
```bash
# 로그 파일 읽기
Read "$APPDATA/mailX/logs/YYYYMMDD.log"

# 또는 REST API
curl -s "http://localhost:5858/api/logs/latest?lines=100"
```

**통과 조건**:
- ERROR/WARN/Exception 0건
- Debug 로그 정상 출력

**실패 시**: 계획 수립 단계부터 전체 사이클 재시작

#### 2단계: REST API 테스트 (필수)
```bash
curl -s http://localhost:5858/api/health
curl -s http://localhost:5858/api/status
```

**통과 조건**:
- HTTP 200 응답
- 데이터 정확성 100%

**실패 시**: 계획 수립 단계부터 전체 사이클 재시작

#### 3단계: 스크린샷 검증 (필수)
```bash
# 스크린샷 캡처
curl -X POST http://localhost:5858/api/screenshot

# 이미지 확인
Read "$APPDATA/mailX/screenshots/screenshot_*.png"
```

**통과 조건**:
- 창 정상 표시
- 한글 텍스트 정상 렌더링
- UI 컨트롤 배치 정확

**실패 시**: 계획 수립 단계부터 전체 사이클 재시작

---

## 작업 완료 체크리스트

### C# 소스 수정 시

```yaml
1_프로그램_종료:
  curl -X POST http://localhost:5858/api/shutdown -H "Content-Type: application/json" -d '{"force":false}'

2_로그_삭제:
  rm -f "$APPDATA/mailX/logs"/*.log

3_빌드:
  dotnet build "C:\DATA\Project\mailX\mailX\mailX.csproj"

4_실행:
  "C:\DATA\Project\mailX\mailX\bin\Debug\net10.0-windows\mailX.exe" > /dev/null 2>&1 &
  sleep 3
  curl -s http://localhost:5858/api/health

5_테스트_3단계:
  - 1단계: 로그 분석 (ERROR 0건)
  - 2단계: REST API 테스트 (HTTP 200)
  - 3단계: 스크린샷 검증 (UI 정상)
```

---

## REST API 엔드포인트

| 메서드 | 경로 | 설명 |
|--------|------|------|
| GET | `/api/health` | 헬스 체크 |
| GET | `/api/status` | 앱 상태 조회 |
| POST | `/api/shutdown` | 앱 종료 |
| POST | `/api/shutdown/force` | 강제 종료 |
| POST | `/api/screenshot` | 스크린샷 촬영 |
| GET | `/api/logs/latest` | 최신 로그 조회 |

---

## MCP 서버 설정

### 기본 ON
| 서버 | 용도 |
|------|------|
| **mysql** | DB 작업 |
| **serena** | 코드 분석/편집 |
| **sequential-thinking** | 문제 분석 |
| **context7** | 라이브러리 문서 |
| **ref** | 문서 검색 |
| **vibe-check-mcp** | 메타인지 검증 |

---

## 주의사항

### Bash 명령어 단일 실행 원칙
- `&&`, `||`, `;` 연산자 사용 금지
- **한 번의 Bash 호출에 하나의 명령만 실행**
- 예: 로그 삭제 후 앱 실행 → 로그 삭제 Bash 호출 + 앱 실행 Bash 호출 (별도)
- 각 명령어를 별도의 Bash 호출로 분리

### 사용자 확인 정책
- "확인해주세요", "테스트해주세요" 등 절대 금지
- AI가 직접 3단계 테스트로 검증

### 작업 마무리 시 문서화 필수
```yaml
필수:
  - 모든 작업 완료 시 관련 내용을 CLAUDE.md에 업데이트
  - 새로운 기능, 패턴, API, 컨버터 등 추가 시 문서화
  - 중요한 구현 세부사항 (파일 위치, 라인 번호 등) 기록

문서화_대상:
  - 새로운 서비스/기능 구현
  - Graph API 연동 추가
  - UI 컴포넌트/애니메이션 패턴
  - 컨버터, 헬퍼 클래스 추가
  - DB 스키마 변경
  - 설정 파일 구조 변경

목적:
  - 다음 세션에서 컨텍스트 유지
  - 코드 구조 파악 용이
  - 일관된 패턴 유지
```

---

## 주요 기능 구현 현황

### 캘린더/일정 관리 기능
```yaml
구현_완료:
  - 좌측 아이콘바 (메일/캘린더 모드 전환)
  - 월간 캘린더 뷰 (아웃룩 스타일)
  - 일정 CRUD (생성/조회/수정/삭제)
  - 일정 알림 (ntfy 푸시)
  - 우측 세부일정 패널

관련_파일:
  - Views/CalendarView (MainWindow.xaml 내 CalendarViewBorder)
  - Views/Dialogs/EventEditDialog.xaml
  - Services/Graph/GraphCalendarService.cs
  - ViewModels/CalendarViewModel.cs

Azure_AD_권한_필요:
  - Calendars.Read
  - Calendars.ReadWrite
  # Azure Portal에서 앱 등록에 권한 추가 필요
```

### 인증 시스템 (MSAL)
```yaml
토큰_캐시_경로: "%LocalAppData%/mailX/msal_token_cache.bin"
설정_파일: "%APPDATA%/mailX/conf/autologin.xml"

권한_변경_시:
  1. GraphAuthService.cs DefaultScopes 수정
  2. appsettings.json Scopes 수정
  3. Azure Portal 앱 등록에서 권한 추가
  4. 토큰 캐시 삭제 후 재로그인

현재_권한:
  - User.Read
  - Mail.Read, Mail.Send, Mail.ReadWrite
  - Files.Read.All, Sites.Read.All
  - Calendars.Read, Calendars.ReadWrite
```

### 동기화 시스템
```yaml
BackgroundSyncService:
  주기: 5분
  동기화_순서:
    1. 폴더 동기화 (SyncFoldersAsync)
    2. 메일 동기화 (SyncAllAccountsAsync)
    3. 캘린더 동기화 (SyncCalendarAsync)
    4. 캘린더 알림 체크 (CheckCalendarRemindersAsync)

이벤트:
  - FoldersSynced: 폴더 동기화 완료
  - EmailsSynced: 메일 동기화 완료 (새 메일 수)
  - CalendarSyncStarted: 캘린더 동기화 시작
  - CalendarSyncProgress: 캘린더 동기화 진행 (current, total, stepName)
  - CalendarSynced: 캘린더 동기화 완료 (일정 수)
```

### Graph API 실시간 동기화 (mailX ↔ Outlook)
```yaml
읽음_상태_동기화:
  mailX_to_Outlook: MainViewModel.UpdateReadStatusAsync() → GraphMailService.UpdateMessageReadStatusAsync()
  Outlook_to_mailX: BackgroundSyncService (5분 주기, deltaLink 사용)

삭제_동기화:
  mailX_to_Outlook: MainViewModel.DeleteEmailAsync() → GraphMailService.MoveMessageAsync() (휴지통) 또는 DeleteMessageAsync() (영구삭제)
  Outlook_to_mailX: BackgroundSyncService (5분 주기)

폴더_이동_동기화:
  mailX_to_Outlook: GraphMailService.MoveMessageAsync(messageId, destinationFolderId)
  Outlook_to_mailX: BackgroundSyncService (5분 주기)

플래그_동기화:
  mailX_to_Outlook: MainViewModel.UpdateFlagStatusAsync() → GraphMailService.UpdateMessageFlagAsync()
  상태값: flagged, complete, notFlagged
  Outlook_to_mailX: BackgroundSyncService (5분 주기)

핀_기능:
  로컬_전용: IsPinned, PinnedAt 컬럼 (Graph API 미지원)
  정렬: 핀 메일 상단 고정 (PinnedAt DESC)
```

### UI 애니메이션 패턴
```yaml
AI_분석_버튼:
  진행중: 노랑색 별 3개 (✦) 각기 다른 속도로 깜빡임
    - 큰별: #FFD700, 0.8초 주기
    - 중간별: #FFC125, 0.5초 주기, 0.2초 지연
    - 작은별: #FFDF00, 0.3초 주기, 0.4초 지연
  중지중: 회색 별 3개 (#888888, #999999, #AAAAAA) 고정

메일_동기화_버튼:
  동기화중: 열린 편지 (MailRead24) + 문서(📄) 나가는 애니메이션
    - 1.5초 주기
    - QuadraticEase EaseOut
  중지중: 회색 닫힌 편지 (Mail24) 고정

AI_분석_패널_헤더:
  위치: 메일 본문 상단 "AI 분석" 텍스트 앞
  구현: AI 분석 버튼과 동일한 3색 별 애니메이션

애니메이션_구현_위치:
  - MainWindow.xaml: 라인 1193-1322 (동기화 버튼들)
  - MainWindow.xaml: 라인 2309-2365 (AI 분석 패널 헤더)
```

### Converters 목록
```yaml
위치: mailX/Converters/

BoolToVisibilityConverter:
  - True → Visible, False → Collapsed
  - ConverterParameter="Invert" 시 반전

BoolToAppearanceConverter:
  - True → Primary, False → Secondary
  - 토글 버튼 상태 표시용

BoolToForegroundConverter:
  - 핀 버튼 등 상태별 색상 변경

FlagStatusToAppearanceConverter:
  - flagged → Primary, 그 외 → Secondary

FlagStatusToVisibilityConverter:
  - 플래그 상태에 따른 아이콘 표시

CategoryToColorConverter:
  - 카테고리명 → 색상 매핑

ImportanceToVisibilityConverter:
  - high → Visible, 그 외 → Collapsed

SortByToAppearanceConverter:
  - 정렬 버튼 활성 상태 표시
```

---

## 동기화 주기별 사용자 규모 영향 분석

### 아키텍처
```yaml
구조:
  - mailX: 로컬 WPF 앱 (각 PC에서 독립 실행)
  - API_호출: 각 사용자가 개별적으로 Microsoft Graph API / AI API 호출
  - 인증: 각 사용자의 Microsoft 365 계정 토큰 사용
```

### 1회 동기화당 API 호출
| 구분 | 호출 횟수 |
|------|----------|
| 메일 Delta Query | 5~15회 (폴더당 1회) |
| 폴더 동기화 | 3~10회 (5분마다) |
| 캘린더 동기화 | 3회 (5분마다) |
| **합계** | 약 9~35회/동기화 |

### AI 분석 (새 메일 도착 시)
- 메일 1건당 **7회 API 호출** (7단계 분석 파이프라인)
- 최대 동시 처리: 5건

### Rate Limit 상세
```yaml
Microsoft_Graph_API:
  앱당: 2,000회/분
  테넌트당: 10,000회/분
  사용자당: 10,000회/10분

AI_API:
  Claude: 100,000 tokens/분 (플랜에 따라 상이)
  OpenAI_GPT4o: 500,000 tokens/분
  Ollama_로컬: 무제한
```

### 사용자 규모별 권장 동기화 주기

| 사용자 수 | 동기화 주기 | AI 분석 | 비고 |
|----------|-----------|--------|------|
| 1인 (개발/테스트) | 10초~30초 | 즉시 | 테스트용 가능 |
| 1~10인 | 1분~5분 | 즉시 | 안정적 운영 |
| 10~50인 | 5분 | 즉시 | 표준 권장 |
| 50~100인 | 5분~10분 | 즉시 + 큐잉 고려 | 모니터링 필요 |
| 100인+ | 10분 이상 | 별도 서버 고려 | 아키텍처 변경 권장 |

### 주기별 안전성 요약

| 주기 | 1인 | 10인 | 100인 |
|------|-----|------|-------|
| 1초 | ⚠️ 가능 | ❌ 위험 | ❌ 불가 |
| 5초 | ✅ 가능 | ⚠️ 주의 | ❌ 불가 |
| 10초 | ✅ 안전 | ✅ 가능 | ❌ 위험 |
| 30초 | ✅ 안전 | ✅ 안전 | ⚠️ 주의 |
| 1분 | ✅ 안전 | ✅ 안전 | ✅ 가능 |
| 5분 | ✅ 권장 | ✅ 권장 | ✅ 권장 |

### 100인+ 대응 아키텍처 제안
```yaml
1_중앙_동기화_서버: 각 PC 대신 서버에서 동기화 → DB 배포
2_WebSocket_푸시: Delta Query 대신 Graph Webhook 구독
3_AI_큐_시스템: 분석 요청을 큐에 넣고 순차 처리
```
