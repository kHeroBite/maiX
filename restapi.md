# REST API 문서

mailX 애플리케이션의 REST API는 5858 포트에서 실행되는 HTTP 서버로, 외부에서 프로그램을 제어하고 테스트 자동화를 지원합니다.

## 목차

1. [기본 정보](#기본-정보)
2. [한글 인코딩 처리](#한글-인코딩-처리)
3. [상태 확인](#상태-확인)
4. [프로그램 제어](#프로그램-제어)
5. [스크린샷](#스크린샷)
6. [로그 조회](#로그-조회)
7. [테스트 자동화](#테스트-자동화)
8. [오류 처리](#오류-처리)

---

## 기본 정보

### 서버 정보
- **URL**: `http://localhost:5858`
- **포트**: 5858 (충돌 시 자동으로 5859, 5860... 시도)
- **프로토콜**: HTTP
- **응답 형식**: JSON (UTF-8)

### 소스 파일
- `mailX/Services/Api/RestApiServer.cs`

### CORS
모든 엔드포인트에서 CORS가 허용됩니다:
- `Access-Control-Allow-Origin: *`
- `Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS`
- `Access-Control-Allow-Headers: Content-Type`

---

## 한글 인코딩 처리

### 문제
curl -d 옵션으로 한글 데이터 직접 전달 시 인코딩 오류 발생

### 원인
bash 쉘 인코딩 설정과 curl 인코딩 처리 방식 불일치

### 금지 방식 (직접 전달)

```bash
# 틀린 예시
curl -X POST http://localhost:5858/api/someendpoint \
  -H "Content-Type: application/json" \
  -d '{"name":"한글데이터"}'
```

**문제**: "한글데이터" 깨짐

### 권장 방식 (파일 사용)

```bash
# 올바른 예시

# 1. JSON 파일 생성 (UTF-8 인코딩 보장)
echo '{"name":"한글데이터"}' > /tmp/data.json

# 2. 파일 전달 (--data-binary 사용)
curl -X POST http://localhost:5858/api/someendpoint \
  -H "Content-Type: application/json; charset=utf-8" \
  --data-binary @/tmp/data.json

# 3. 정리
rm /tmp/data.json
```

### 적용 규칙
- 한글 포함: 파일 방식 필수
- ASCII만: curl -d 옵션 사용 가능
- 의심스러움: 파일 방식 (안전성 우선)

---

## 상태 확인

### GET /api/health

프로그램 헬스 체크 (가장 기본적인 상태 확인)

**요청**:
```bash
curl -s http://localhost:5858/api/health
```

**응답 (HTTP 200)**:
```json
{
  "status": "healthy",
  "timestamp": "2026-01-13 15:30:45",
  "port": 5858
}
```

**필드 설명**:
| 필드 | 타입 | 설명 |
|------|------|------|
| status | string | 항상 "healthy" (정상 상태) |
| timestamp | string | 현재 시각 (yyyy-MM-dd HH:mm:ss) |
| port | int | REST API 서버 포트 |

---

### GET /api/status

프로그램 상세 상태 조회 (로그 카운트, 메인 창 상태 포함)

**요청**:
```bash
curl -s http://localhost:5858/api/status
```

**응답 (HTTP 200)**:
```json
{
  "status": "running",
  "timestamp": "2026-01-13 15:30:45",
  "port": 5858,
  "logCount": 42,
  "debugMode": true,
  "mainWindow": {
    "state": "Normal",
    "visible": true
  }
}
```

**필드 설명**:
| 필드 | 타입 | 설명 |
|------|------|------|
| status | string | "running" (실행 중) |
| logCount | int | 현재 세션 로그 라인 수 |
| debugMode | bool | 디버그 모드 활성화 여부 |
| mainWindow.state | string | 창 상태 (Normal, Minimized, Maximized) |
| mainWindow.visible | bool | 창 표시 여부 |

---

## 프로그램 제어

### POST /api/shutdown

프로그램 정상 종료

**요청**:
```bash
# 정상 종료 (권장)
curl -X POST http://localhost:5858/api/shutdown \
  -H "Content-Type: application/json" \
  -d '{"force":false}'
```

**요청 본문**:
| 필드 | 타입 | 기본값 | 설명 |
|------|------|--------|------|
| force | bool | false | true: 즉시 종료, false: 정상 종료 |

**응답 (HTTP 200)**:
```json
{
  "message": "Shutdown initiated",
  "force": false
}
```

**참고**:
- `force: false`: `Application.Shutdown()` 호출 (정상 종료)
- `force: true`: `Environment.Exit(0)` 호출 (즉시 종료)

---

### POST /api/shutdown/force

프로그램 강제 종료 (즉시)

**요청**:
```bash
curl -X POST http://localhost:5858/api/shutdown/force
```

**응답 (HTTP 200)**:
```json
{
  "message": "Force shutdown initiated",
  "force": true
}
```

**주의**: 이 엔드포인트는 요청 후 즉시 프로세스가 종료됩니다.

---

## 스크린샷

### POST /api/screenshot

메인 창 스크린샷 캡처

**요청**:
```bash
curl -X POST http://localhost:5858/api/screenshot
```

**응답 (HTTP 200)**:
```json
{
  "message": "Screenshot captured",
  "filePath": "C:\\Users\\{user}\\AppData\\Roaming\\mailX\\screenshots\\screenshot_20260113_153045.png",
  "capturedAt": "2026-01-13 15:30:45"
}
```

**저장 경로**: `%APPDATA%\mailX\screenshots\screenshot_{yyyyMMdd_HHmmss}.png`

**오류 응답 (HTTP 503)** - 메인 창 미초기화:
```json
{
  "error": "메인 창이 아직 초기화되지 않았습니다."
}
```

**스크린샷 검증**:
```bash
# Read 도구로 이미지 확인
Read "$APPDATA/mailX/screenshots/screenshot_*.png"
```

---

## 로그 조회

### GET /api/logs/latest

오늘 날짜 로그 파일의 최신 내용 조회

**요청**:
```bash
# 기본 50줄
curl -s "http://localhost:5858/api/logs/latest"

# 최근 100줄 (최대 500줄)
curl -s "http://localhost:5858/api/logs/latest?lines=100"
```

**쿼리 파라미터**:
| 파라미터 | 타입 | 기본값 | 최대값 | 설명 |
|----------|------|--------|--------|------|
| lines | int | 50 | 500 | 조회할 로그 줄 수 |

**응답 (HTTP 200)**:
```json
{
  "lines": 50,
  "logPath": "C:\\Users\\{user}\\AppData\\Roaming\\mailX\\logs\\20260113.log",
  "logs": [
    "[2026-01-13 15:30:00] INFO  - [RestAPI] 서버 시작 완료 - http://localhost:5858/",
    "[2026-01-13 15:30:01] DEBUG - [RestAPI] GET /api/health",
    "..."
  ]
}
```

**오류 응답 (HTTP 404)** - 로그 파일 없음:
```json
{
  "error": "오늘 로그 파일이 없습니다.",
  "path": "C:\\Users\\{user}\\AppData\\Roaming\\mailX\\logs\\20260113.log"
}
```

---

## 테스트 자동화

### 3단계 테스트 플로우

mailX 프로젝트에서는 모든 코드 변경 후 다음 3단계 테스트를 **순서대로 모두 통과**해야 합니다.

#### 1단계: 로그 분석

```bash
# 방법 A: REST API로 로그 조회
curl -s "http://localhost:5858/api/logs/latest?lines=100"

# 방법 B: 로그 파일 직접 읽기
Read "$APPDATA/mailX/logs/20260113.log"
```

**통과 조건**:
- ERROR/WARN/Exception 로그 0건
- Debug 로그에서 예상 함수 호출 순서 확인

---

#### 2단계: REST API 테스트

```bash
# 헬스 체크
curl -s http://localhost:5858/api/health

# 상태 확인
curl -s http://localhost:5858/api/status
```

**통과 조건**:
- 모든 API 호출 성공 (HTTP 200)
- 응답 데이터 정확성 100% 검증

---

#### 3단계: 스크린샷 테스트

```bash
# 스크린샷 캡처
curl -X POST http://localhost:5858/api/screenshot

# 이미지 검증 (Read 도구)
Read "$APPDATA/mailX/screenshots/screenshot_*.png"
```

**통과 조건**:
- 창 정상 표시
- 한글 텍스트 정상 렌더링
- UI 컨트롤 배치 정확

---

### 자동화 스크립트 예시

```bash
#!/bin/bash

echo "=== mailX 테스트 자동화 ==="

# 0. 기존 프로그램 종료
echo "0단계: 기존 프로그램 종료"
curl -X POST http://localhost:5858/api/shutdown -H "Content-Type: application/json" -d '{"force":false}' 2>/dev/null || true
sleep 2

# 0-1. 로그 삭제
echo "0-1단계: 로그 삭제"
rm -f "$APPDATA/mailX/logs"/*.log

# 0-2. 빌드
echo "0-2단계: 빌드"
dotnet build "C:\DATA\Project\mailX\mailX\mailX.csproj"

# 0-3. 프로그램 실행
echo "0-3단계: 프로그램 실행"
"C:\DATA\Project\mailX\mailX\bin\Debug\net10.0-windows\mailX.exe" &
sleep 5

# 1단계: 로그 분석
echo "1단계: 로그 분석"
ERROR_COUNT=$(curl -s "http://localhost:5858/api/logs/latest?lines=100" | grep -ci "error" || echo "0")
echo "ERROR 카운트: $ERROR_COUNT"

# 2단계: REST API 테스트
echo "2단계: REST API 테스트"
curl -s http://localhost:5858/api/health
curl -s http://localhost:5858/api/status

# 3단계: 스크린샷 테스트
echo "3단계: 스크린샷 테스트"
curl -X POST http://localhost:5858/api/screenshot

echo "=== 테스트 완료 ==="
```

---

## 오류 처리

### HTTP 상태 코드

| 코드 | 설명 | 예시 |
|------|------|------|
| 200 | 성공 | 정상 응답 |
| 404 | Not Found | 존재하지 않는 엔드포인트 |
| 500 | Internal Server Error | 서버 내부 오류 |
| 503 | Service Unavailable | 서비스 준비 안됨 (예: 메인 창 미초기화) |

### 오류 응답 형식

```json
{
  "error": "오류 메시지",
  "message": "상세 설명 (선택)"
}
```

### 404 응답 예시

```json
{
  "error": "Not Found",
  "path": "/api/unknown"
}
```

---

## API 엔드포인트 요약

| 메서드 | 경로 | 설명 |
|--------|------|------|
| GET | /api/health | 헬스 체크 |
| GET | /api/status | 앱 상태 조회 |
| POST | /api/shutdown | 앱 종료 |
| POST | /api/shutdown/force | 앱 강제 종료 |
| POST | /api/screenshot | 스크린샷 캡처 |
| GET | /api/logs/latest | 최신 로그 조회 |

---

## 확장 예정

향후 추가될 수 있는 엔드포인트:
- `GET /api/emails` - 이메일 목록 조회
- `GET /api/emails/{id}` - 이메일 상세 조회
- `POST /api/emails/sync` - 이메일 동기화 트리거
- `GET /api/settings` - 설정 조회
- `PUT /api/settings` - 설정 변경
