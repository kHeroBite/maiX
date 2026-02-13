﻿﻿@echo off
chcp 65001 >nul
echo ============================================
echo  MaiX 공용 파일 심볼릭 링크 생성 스크립트
echo ============================================
echo.

set "MAIX=C:\DATA\Project\MaiX"
set "AI=C:\DATA\Project\AI"

echo [1/3] CLAUDE.md 심볼릭 링크
if exist "%MAIX%\CLAUDE.md" del "%MAIX%\CLAUDE.md"
mklink "%MAIX%\CLAUDE.md" "%AI%\CLAUDE.md"
echo.

echo [2/3] .claude\commands 심볼릭 링크
if exist "%MAIX%\.claude\commands\git-log.md" del "%MAIX%\.claude\commands\git-log.md"
mklink "%MAIX%\.claude\commands\git-log.md" "%AI%\.claude\commands\git-log.md"

if exist "%MAIX%\.claude\commands\git-revert.md" del "%MAIX%\.claude\commands\git-revert.md"
mklink "%MAIX%\.claude\commands\git-revert.md" "%AI%\.claude\commands\git-revert.md"
echo.

echo [3/3] .claude\skills 심볼릭 링크 (공용 33개)
for %%S in (kO kO_gate kPlan kPlan_deep kPlan_sim kPlan_review kDev kDev_parallel kDev_review kDev_impact kDev_lock kTest kTest_build kTest_deploy kTest_run kTest_quality kDone kDone_review kDone_cleanup kDone_docs kDone_skills kDone_hooks kDone_git kDone_notify kDebug kVerify kThink kSurl agent_profiles domain-csharp domain-winforms domain-database domain-context7) do (
    echo   %%S ...
    if exist "%MAIX%\.claude\skills\%%S" rmdir /s /q "%MAIX%\.claude\skills\%%S"
    mklink /D "%MAIX%\.claude\skills\%%S" "%AI%\.claude\skills\%%S"
)
echo.

echo ============================================
echo  settings.json 심볼릭 링크
echo ============================================
if exist "%MAIX%\.claude\settings.json" del "%MAIX%\.claude\settings.json"
mklink "%MAIX%\.claude\settings.json" "%AI%\.claude\settings.json"
echo.

echo ============================================
echo  User scope: hooks 심볼릭 링크
echo ============================================
set "USERCLAUDE=%USERPROFILE%\.claude"
if exist "%USERCLAUDE%\hooks" (
    dir /AL "%USERCLAUDE%\hooks" >nul 2>&1
    if errorlevel 1 (
        echo   기존 hooks 디렉토리를 hooks_backup으로 이동...
        move "%USERCLAUDE%\hooks" "%USERCLAUDE%\hooks_backup" >nul
        mklink /D "%USERCLAUDE%\hooks" "%AI%\.claude\hooks"
    ) else (
        echo   이미 심볼릭 링크입니다. 스킵.
    )
) else (
    mklink /D "%USERCLAUDE%\hooks" "%AI%\.claude\hooks"
)
echo.

echo ============================================
echo  완료! MaiX 전용 파일은 그대로 유지됨:
echo   - PROJECT.md, ADVANCED.md, DATABASE.md 등
echo   - .claude\skills\kInfra_maix
echo   - .claude\skills\kRules_maix
echo   - .claude\skills\kSkill_livecharts2
echo ============================================
echo.
pause
