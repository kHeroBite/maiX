@echo off
chcp 65001 > nul
setlocal enabledelayedexpansion

:: MARS 배포 스크립트
:: 사용법: deploy.bat [버전] [옵션]
:: 예시: deploy.bat                       (자동 버전 증가)
::       deploy.bat 1.1.2                 (버전 지정)
::       deploy.bat minor                 (minor 버전 증가)
::       deploy.bat -Version "1.1.2"      (명시적 버전 지정)
::       deploy.bat -Version "Major"      (major 버전 증가)
::       deploy.bat minor -SkipUpload     (minor 증가, 업로드 스킵)

cd /d "%~dp0"

:: 도움말 표시
if /i "%~1"=="--help" goto :help
if /i "%~1"=="-h" goto :help
if /i "%~1"=="/?" goto :help

:: 모든 인자를 PowerShell에 그대로 전달
if "%~1"=="" (
    echo [MARS] 자동 버전 증가 모드로 배포 시작...
    powershell -ExecutionPolicy Bypass -File "Deploy\Build-Release.ps1"
) else if /i "%~1"=="-Version" (
    :: -Version 플래그가 명시적으로 사용된 경우
    echo [MARS] 버전 %~2 으로 배포 시작...
    powershell -ExecutionPolicy Bypass -File "Deploy\Build-Release.ps1" %*
) else if /i "%~1"=="-SkipUpload" (
    :: -SkipUpload만 사용된 경우
    echo [MARS] 자동 버전 증가 + 업로드 스킵 모드로 배포 시작...
    powershell -ExecutionPolicy Bypass -File "Deploy\Build-Release.ps1" %*
) else (
    :: 첫 번째 인자가 버전 값인 경우 (minor, major, 1.0.0 등)
    echo [MARS] 버전 %~1 으로 배포 시작...
    powershell -ExecutionPolicy Bypass -File "Deploy\Build-Release.ps1" -Version "%~1" %2 %3 %4 %5
)

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] 배포 실패!
    pause
    exit /b 1
)

echo.
echo [MARS] 배포 완료!
pause
goto :eof

:help
echo.
echo  MARS 클라이언트 배포 스크립트
echo  ==============================
echo.
echo  사용법: deploy.bat [버전] [옵션]
echo.
echo  버전 지정:
echo    (없음)          패치 버전 자동 증가 (0.8.7 -^> 0.8.8)
echo    patch           패치 버전 증가 (명시적)
echo    minor           마이너 버전 증가 (0.8.7 -^> 0.9.0)
echo    major           메이저 버전 증가 (0.8.7 -^> 1.0.0)
echo    X.Y.Z           지정 버전 사용 (기존 릴리스 덮어쓰기)
echo.
echo  옵션:
echo    -SkipBuild      빌드 건너뛰기 (기존 빌드 사용)
echo    -SkipUpload     GitHub 업로드 건너뛰기 (로컬 빌드만)
echo.
echo  예시:
echo    deploy.bat                    자동 패치 버전 증가 후 배포
echo    deploy.bat minor              마이너 버전 증가 후 배포
echo    deploy.bat 1.0.0              버전 1.0.0으로 배포
echo    deploy.bat -SkipUpload        로컬 빌드만 (업로드 안함)
echo    deploy.bat minor -SkipUpload  마이너 증가, 업로드 안함
echo.
echo  출력 경로: Deploy\Output\
echo  GitHub: https://github.com/kHeroBite/MarsDeploy
echo.
goto :eof
