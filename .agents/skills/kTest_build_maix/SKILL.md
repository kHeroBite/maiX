---
name: kTest_build_maix
description: "MaiX 프로젝트 빌드 명령/설정. kTest_build에서 자동 로딩."
---
# kTest_build_maix — MaiX 빌드 설정

## WSL 방식 vs Windows 방식

> WPF(.NET Windows) 프로젝트는 WSL dotnet으로 빌드 불가.

```yaml
WSL_dotnet (금지):
  - dotnet build /mnt/c/... → NETSDK1100 오류
  - dotnet build "C:\..." → WSL dotnet이 Windows 경로 인식 불가
  이유: net10.0-windows 타겟은 Windows SDK 필요

Windows_dotnet (필수):
  - cmd.exe /c "dotnet build \"C:\DATA\Project\MaiX\MaiX\MaiX.csproj\""
```

## 빌드 명령어

```yaml
MaiX:
  cmd.exe /c "dotnet build \"C:\DATA\Project\MaiX\MaiX\MaiX.csproj\""
```

## 빌드 전 필수: 프로세스 종료

```yaml
이유: 실행 중인 MaiX.exe가 DLL을 잠금 → dotnet build 파일 잠금 실패
방법:
  1단계: /mnt/c/Windows/System32/curl.exe -s -X POST http://localhost:5858/api/shutdown -H "Content-Type: application/json" -d "{\"force\":false}"
  2단계: sleep 3
  3단계: tasklist.exe 2>/dev/null | grep -i MaiX 로 잔존 확인
  4단계: 잔존 시 taskkill.exe /F /IM MaiX.exe
```

## 빌드 실행 순서

```yaml
전제: 빌드는 항상 배포 순서의 일부로 실행 (kTest_deploy_maix 참조)
순서: 종료 → 빌드 → 로그삭제 → 실행 → 헬스체크
참조: kTest_deploy_maix "배포 순서" 섹션이 유일 출처 (Single Source of Truth)
```

## 성공/실패 판단

```yaml
성공: "Build succeeded" + "오류 0개"
실패: "Build FAILED" 또는 "error" 포함
```
