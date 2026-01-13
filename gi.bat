@echo off
setlocal

where git >nul 2>&1 || (
  echo [ERROR] Git not found in PATH
  exit /b 1
)

git rev-parse --is-inside-work-tree >nul 2>&1 || (
  echo [ERROR] Not a git repository
  exit /b 1
)

powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0gi.ps1"

set ps_exit=%ERRORLEVEL%

if %ps_exit% NEQ 0 (
  if %ps_exit% EQU 2 (
    echo [INFO] No reflog entries found
  ) else (
    echo [ERROR] PowerShell script failed with code %ps_exit%
  )
)
endlocal
