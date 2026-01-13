# CLAUDE.md - AI Agent 프로젝트 매뉴얼

> **역할**: Claude Code가 mailX 프로젝트에서 작업할 때 필수로 참조해야 하는 핵심 지침서

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

실행: "C:\DATA\Project\mailX\mailX\bin\Debug\net10.0-windows\mailX.exe" > /dev/null 2>&1 &

종료: curl -X POST http://localhost:5858/api/shutdown -H "Content-Type: application/json" -d '{"force":false}'

로그_삭제: rm -f "$APPDATA/mailX/logs"/*.log

로그_확인: Read $APPDATA/mailX/logs/YYYYMMDD.log

ntfy_알림: |
  echo '{"topic":"mailX","title":"제목","message":"내용"}' > /tmp/ntfy.json
  curl -X POST https://ntfy.sh -H "Content-Type: application/json" --data-binary @/tmp/ntfy.json
  rm /tmp/ntfy.json

Git_작업:
  1. git add .
  2. git commit -m "커밋 메시지"
  3. git push

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
- `&&`, `||`, `;`, `|` 연산자 사용 금지
- 각 명령어를 별도의 Bash 호출로 분리

### 사용자 확인 정책
- "확인해주세요", "테스트해주세요" 등 절대 금지
- AI가 직접 3단계 테스트로 검증
