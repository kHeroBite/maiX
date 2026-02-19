---
name: kInfra_maix
description: "MaiX 프로젝트 인프라 설정. kO 시작 시 자동 로딩."
---
# kInfra_maix — MaiX 프로젝트 인프라 설정

kO 진입 시 자동 로딩되는 MaiX 프로젝트 전용 설정.

---

## 프로젝트 메타

### 솔루션 경로

```yaml
솔루션: "C:\DATA\Project\MaiX\MaiX.sln"
프로젝트:
  MaiX: "C:\DATA\Project\MaiX\MaiX\MaiX.csproj"
실행파일: "C:\DATA\Project\MaiX\MaiX\bin\Debug\net10.0-windows\MaiX.exe"
프레임워크: .NET 10.0-windows (WPF)
UI_라이브러리: WPF UI (Fluent Design)
데이터베이스: SQLite (EF Core)
```

### API 서버

```yaml
포트: 5858
헬스체크: /mnt/c/Windows/System32/curl.exe -s http://localhost:5858/api/health
종료: /mnt/c/Windows/System32/curl.exe -s -X POST http://localhost:5858/api/shutdown -H "Content-Type: application/json" -d "{\"force\":false}"
실행: powershell.exe -NoProfile -Command "Start-Process 'C:\DATA\Project\MaiX\MaiX\bin\Debug\net10.0-windows\MaiX.exe'"

curl_규칙:
  필수: /mnt/c/Windows/System32/curl.exe (Windows curl) 사용
  금지: WSL curl (localhost 연결 불가, exit code 7)
  이유: WSL2에서 WSL curl은 Windows localhost에 접근 불가

앱_종료_원칙:
  필수: 항상 REST API(Windows curl.exe)로 종료
  금지: taskkill, Stop-Process 등 강제 종료 명령
  이유: REST API 종료는 앱의 정상적인 종료 프로세스를 보장
```

### 환경변수

```yaml
APPDATA: /mnt/c/Users/rioky/AppData/Roaming
```

### 로그 / 스크린샷

```yaml
로그_경로: /mnt/c/Users/rioky/AppData/Roaming/MaiX/logs/YYYYMMDD.log
로그_API: http://localhost:5858/api/logs/latest?lines=100
스크린샷: /mnt/c/Windows/System32/curl.exe -s -X POST http://localhost:5858/api/screenshot -H "Content-Length: 0"
스크린샷_경로: /mnt/c/Users/rioky/AppData/Roaming/MaiX/screenshots/
도구: /mnt/c/Windows/System32/curl.exe (Windows curl 필수, WSL curl 사용 금지)
```

### ntfy 알림

```yaml
토픽: MaiX
발송:
  echo '{"topic":"MaiX","title":"제목","message":"내용","tags":["tag"]}' > /tmp/ntfy.json
  curl -X POST https://ntfy.sh -d @/tmp/ntfy.json -H "Content-Type: application/json"
금지: -H "Title:" 헤더 방식 (한글 깨짐)
```

### Git Push

```yaml
명령: git push (WSL git 네이티브 사용)
인증: Windows GCM (credential.helper)
원칙: WSL에서 최대한 리눅스 도구 사용, Windows 도구는 최소화
```

### 관련 문서

- [ADVANCED.md](./ADVANCED.md): 확장 가이드
- [PROJECT.md](./PROJECT.md): 프로젝트 구조 및 파일 인벤토리
- [DATABASE.md](./DATABASE.md): 테이블, 스키마
- [MCP.md](./MCP.md): MCP 서버 통합 문서
- [RESTAPI.md](./restapi.md): REST API 엔드포인트

### 코딩 규칙

```yaml
참조: /kRules_maix
내용: WPF MVVM 패턴, XAML 바인딩, 네임스페이스, DI 등 프로젝트 고유 규칙
```

---

## 빌드 설정

### WSL 방식 vs Windows 방식

> WPF(.NET Windows) 프로젝트는 반드시 Windows dotnet으로 빌드/실행해야 함.

```yaml
WSL_방식 (금지 — NETSDK1100 오류):
  - dotnet build /mnt/c/DATA/Project/MaiX/MaiX/MaiX.csproj
  - dotnet build "C:\DATA\Project\MaiX\MaiX\MaiX.csproj"
  이유: WSL dotnet은 net10.0-windows 타겟 빌드 불가

Windows_방식 (필수):
  빌드: cmd.exe /c "dotnet build \"C:\DATA\Project\MaiX\MaiX\MaiX.csproj\""
  실행: powershell.exe -NoProfile -Command "Start-Process 'C:\DATA\Project\MaiX\MaiX\bin\Debug\net10.0-windows\MaiX.exe'"
  프로세스확인: tasklist.exe 2>/dev/null | grep -i MaiX
  프로세스종료: taskkill.exe /F /IM MaiX.exe

실행_금지:
  - cmd.exe /c start (WSL에서 "접근이 거부되었습니다" 오류)
  - cmd.exe /c "start ..." (동일 오류)

WSL_허용_명령어:
  - curl (외부 서버만 — ntfy.sh 등. localhost 접근 불가)
  - sleep
  - Read/Glob/Grep (파일 읽기)
  - git (네이티브 WSL git 사용)

localhost_접근_필수:
  - /mnt/c/Windows/System32/curl.exe (Windows curl)
  - WSL curl로 localhost 접근 시 exit code 7 (connection refused)
```

### 빌드 명령어

```yaml
MaiX:
  cmd.exe /c "dotnet build \"C:\DATA\Project\MaiX\MaiX\MaiX.csproj\""
```

### 빌드 전 필수: 프로세스 종료

```yaml
이유: 실행 중인 MaiX.exe가 DLL을 잠금 → dotnet build 파일 잠금 실패
방법:
  1단계: /mnt/c/Windows/System32/curl.exe -s -X POST http://localhost:5858/api/shutdown -H "Content-Type: application/json" -d "{\"force\":false}"
  2단계: sleep 3
  3단계: tasklist.exe 2>/dev/null | grep -i MaiX 로 잔존 확인
  4단계: 잔존 시 taskkill.exe /F /IM MaiX.exe
```

---

## 배포 설정

### MaiX (로컬 실행)

```yaml
배포_순서 (필수 — 순서 변경/생략 금지):
  1_프로그램_종료: REST API shutdown (또는 taskkill Fallback)
  2_빌드: dotnet build
  3_로그_삭제: rm -f 당일 로그
  4_실행: PowerShell Start-Process
  5_헬스체크: curl.exe health API
  금지: 로그 삭제 생략 → 이전 빌드 로그와 혼재되어 디버깅 불가
  상세: kTest_deploy_maix 참조
```

---

## 테스트 설정

> 이 섹션이 테스트 절차의 유일 출처 (Single Source of Truth)

### 환경변수

```yaml
APPDATA: /mnt/c/Users/rioky/AppData/Roaming
```

### 1. 로그 확인

```yaml
방법: Read /mnt/c/Users/rioky/AppData/Roaming/MaiX/logs/YYYYMMDD.log
대체: /mnt/c/Windows/System32/curl.exe -s "http://localhost:5858/api/logs/latest?lines=100"

통과_조건:
  - ERROR/WARN/Exception 로그 0건

실패_시: 원인 분석 후 스마트 라우팅
```

### 2. REST API 테스트

```yaml
헬스_체크: /mnt/c/Windows/System32/curl.exe -s http://localhost:5858/api/health
기능별: /mnt/c/Windows/System32/curl.exe -s http://localhost:5858/api/{endpoint}

통과_조건:
  - HTTP 200 응답
  - 데이터 정확성 100%
```

### 3. 스크린샷 확인

```yaml
1_캡처: /mnt/c/Windows/System32/curl.exe -s -X POST http://localhost:5858/api/screenshot -H "Content-Length: 0"
2_확인: Read /mnt/c/Users/rioky/AppData/Roaming/MaiX/screenshots/screenshot_*.png

통과_조건:
  - 창 정상 표시
  - 한글 텍스트 정상 렌더링
  - UI 컨트롤 배치 정확
```

### 4. 통합 테스트 (3-in-1)

```yaml
목적: REST API -> 스크린샷 -> 로그를 한 사이클로 검증

절차:
  1. REST API로 기능 실행 (예: /api/status)
  2. 스크린샷 캡처 및 확인
  3. 로그 확인

통과_조건 (3가지 모두):
  - REST API 응답 정상 (HTTP 200)
  - 스크린샷 UI 정상
  - 로그 ERROR/WARN 0건
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
