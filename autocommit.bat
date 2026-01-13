@echo off
setlocal
SET PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe
"%PS%" -NoProfile -ExecutionPolicy Bypass -File "Tools/AutoCommit/AutoCommit.ps1" %*
