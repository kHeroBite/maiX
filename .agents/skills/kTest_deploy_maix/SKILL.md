---
name: kTest_deploy_maix
description: "MaiX 프로젝트 배포(로컬 실행) 명령/설정. kTest_deploy에서 자동 로딩."
---
# kTest_deploy_maix — MaiX 배포 설정

## WSL 방식 vs Windows 방식

```yaml
WSL_허용:
  - sleep (대기)

localhost_접근_필수:
  - /mnt/c/Windows/System32/curl.exe (WSL curl은 localhost 접근 불가, exit code 7)

Windows_필수:
  실행: powershell.exe -NoProfile -Command "Start-Process 'C:\DATA\Project\MaiX\MaiX\bin\Debug\net10.0-windows\MaiX.exe'"

금지:
  - cmd.exe /c start (WSL에서 "접근이 거부되었습니다" 오류)
  - cmd.exe /c "start ..." (동일 오류)
```

## 배포 순서 (필수 — 순서 변경/생략 금지)

```yaml
배포_순서:
  1_프로그램_종료: REST API shutdown (또는 taskkill Fallback)
  2_빌드: dotnet build
  3_로그_삭제: rm -f 당일 로그
  4_실행: PowerShell Start-Process
  5_헬스체크: curl.exe health API
  금지: 로그 삭제 생략 → 이전 빌드 로그와 혼재되어 디버깅 불가
```

## 배포 절차 (상세)

```yaml
1_종료:
  /mnt/c/Windows/System32/curl.exe -s -X POST http://localhost:5858/api/shutdown -H "Content-Type: application/json" -d "{\"force\":false}"
  sleep 3
  잔존_확인: tasklist.exe 2>/dev/null | grep -i MaiX
  잔존_시: taskkill.exe /F /IM MaiX.exe

2_빌드:
  cmd.exe /c "dotnet build \"C:\DATA\Project\MaiX\MaiX\MaiX.csproj\""

3_로그_삭제:
  rm -f /mnt/c/Users/rioky/AppData/Roaming/MaiX/logs/$(date +%Y%m%d).log

4_실행:
  powershell.exe -NoProfile -Command "Start-Process 'C:\DATA\Project\MaiX\MaiX\bin\Debug\net10.0-windows\MaiX.exe'"

5_헬스체크:
  sleep 8
  /mnt/c/Windows/System32/curl.exe -s http://localhost:5858/api/health
  실패 시: sleep 15 후 재시도

참고: 로그인 창에서 대기 시 REST API 서버 미시작 (로그인 완료 후 시작됨)
```
