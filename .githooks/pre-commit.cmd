@echo off
:: UTF-8 guard pre-commit hook launcher (Windows)
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0pre-commit.ps1"
exit /b %ERRORLEVEL%

