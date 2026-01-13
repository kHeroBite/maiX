param(
    [int]$DebounceSeconds = 5,
    [string[]]$ExcludeDirs = @('.git','bin','obj','.vs'),
    [switch]$VerboseLog
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# 콘솔/출력 인코딩을 UTF-8로 강제
try { chcp 65001 | Out-Null } catch {}
[Console]::InputEncoding  = [System.Text.UTF8Encoding]::new()
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$OutputEncoding           = [System.Text.UTF8Encoding]::new()

# Git 커밋/로그 인코딩을 UTF-8로 설정(로컬 레포)
git config i18n.commitEncoding utf-8 2>$null | Out-Null
git config i18n.logOutputEncoding utf-8 2>$null | Out-Null

function Write-Info($msg){ if($VerboseLog){ Write-Host "[AutoCommit] $msg" -ForegroundColor Cyan } }
function Write-Warn($msg){ Write-Host "[AutoCommit] $msg" -ForegroundColor Yellow }
function Write-Err($msg){ Write-Host "[AutoCommit] $msg" -ForegroundColor Red }

$repoRoot = (git rev-parse --show-toplevel) 2>$null
if (-not $repoRoot) { Write-Err "Git 저장소를 찾을 수 없습니다."; exit 1 }
Set-Location $repoRoot
# 전역 Mutex로 다중 실행 방지
$createdNew = $false
try {
    $globalName = 'Global/MarsAutoCommitWatcher'
    $mutex = New-Object System.Threading.Mutex($true, $globalName, [ref]$createdNew)
} catch { $mutex = $null }
if ($mutex -ne $null -and -not $createdNew) {
    Write-Info 'AutoCommit watcher already running. Skip starting another.'
    exit 0
}
# FileSystemWatcher 설정
$watcher = New-Object System.IO.FileSystemWatcher $repoRoot, '*'
$watcher.IncludeSubdirectories = $true
$watcher.EnableRaisingEvents = $true

# 제외 경로 필터
function Test-Excluded([string]$path){
    foreach($d in $ExcludeDirs){ if($path -like (Join-Path $repoRoot $d) + '*'){ return $true } }
    return $false
}

$pending = $false
$timer = New-Object System.Timers.Timer($DebounceSeconds * 1000)
$timer.AutoReset = $false
$timer.add_Elapsed({
    try {
        if(-not $pending){ return }
        $pending = $false
        Write-Info "변경 감지됨 → git add/commit 수행"

        $status = (git status --porcelain)
        if([string]::IsNullOrWhiteSpace($status)){
            Write-Info "커밋할 변경 없음"
            return
        }

        git add -A | Out-Null

        $msgFile = Join-Path $repoRoot '.commit_message.txt'
        $msg = ''
        if(Test-Path $msgFile){ $msg = Get-Content $msgFile -Raw -ErrorAction SilentlyContinue }
                $msg = ($msg | ForEach-Object { param(
    [int]$DebounceSeconds = 5,
    [string[]]$ExcludeDirs = @('.git','bin','obj','.vs'),
    [switch]$VerboseLog
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# 콘솔/출력 인코딩을 UTF-8로 강제
try { chcp 65001 | Out-Null } catch {}
[Console]::InputEncoding  = [System.Text.UTF8Encoding]::new()
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$OutputEncoding           = [System.Text.UTF8Encoding]::new()

# Git 커밋/로그 인코딩을 UTF-8로 설정(로컬 레포)
git config i18n.commitEncoding utf-8 2>$null | Out-Null
git config i18n.logOutputEncoding utf-8 2>$null | Out-Null

function Write-Info($msg){ if($VerboseLog){ Write-Host "[AutoCommit] $msg" -ForegroundColor Cyan } }
function Write-Warn($msg){ Write-Host "[AutoCommit] $msg" -ForegroundColor Yellow }
function Write-Err($msg){ Write-Host "[AutoCommit] $msg" -ForegroundColor Red }

$repoRoot = (git rev-parse --show-toplevel) 2>$null
if (-not $repoRoot) { Write-Err "Git 저장소를 찾을 수 없습니다."; exit 1 }
Set-Location $repoRoot
# 전역 Mutex로 다중 실행 방지
$createdNew = $false
try {
    $globalName = 'Global/MarsAutoCommitWatcher'
    $mutex = New-Object System.Threading.Mutex($true, $globalName, [ref]$createdNew)
} catch { $mutex = $null }
if ($mutex -ne $null -and -not $createdNew) {
    Write-Info 'AutoCommit watcher already running. Skip starting another.'
    exit 0
}
# FileSystemWatcher 설정
$watcher = New-Object System.IO.FileSystemWatcher $repoRoot, '*'
$watcher.IncludeSubdirectories = $true
$watcher.EnableRaisingEvents = $true

# 제외 경로 필터
function Test-Excluded([string]$path){
    foreach($d in $ExcludeDirs){ if($path -like (Join-Path $repoRoot $d) + '*'){ return $true } }
    return $false
}

$pending = $false
$timer = New-Object System.Timers.Timer($DebounceSeconds * 1000)
$timer.AutoReset = $false
$timer.add_Elapsed({
    try {
        if(-not $pending){ return }
        $pending = $false
        Write-Info "변경 감지됨 → git add/commit 수행"

        $status = (git status --porcelain)
        if([string]::IsNullOrWhiteSpace($status)){
            Write-Info "커밋할 변경 없음"
            return
        }

        git add -A | Out-Null

        $msgFile = Join-Path $repoRoot '.commit_message.txt'
        $msg = ''
        if(Test-Path $msgFile){ $msg = Get-Content $msgFile -Raw -ErrorAction SilentlyContinue }
        $msg = ($msg | ForEach-Object { $_ }).Trim()
        if([string]::IsNullOrWhiteSpace($msg)){
            $msg = '[코덱스] ♻ 자동 커밋: 변경사항 반영'
        } elseif(-not $msg.StartsWith('[코덱스]')){
            $msg = "[코덱스] $msg"
        }

        git commit -m $msg | Out-Null
        Write-Host "자동 커밋 완료: $msg" -ForegroundColor Green
    }
    catch {
        Write-Err $_.Exception.Message
    }
})

$onChange = {
    param($sender, $eventArgs)
    try {
        $fullPath = $eventArgs.FullPath
        if(Test-Excluded $fullPath){ return }
        $pending = $true
        $null = $timer.Stop()
        $null = $timer.Start()
    } catch { Write-Err $_.Exception.Message }
}

$watcher.add_Changed($onChange)
$watcher.add_Created($onChange)
$watcher.add_Deleted($onChange)
$watcher.add_Renamed($onChange)

Write-Host "AutoCommit watcher 시작 (repo: $repoRoot, debounce: ${DebounceSeconds}s)" -ForegroundColor Cyan
Write-Host "종료: Ctrl + C"

try {
    while ($true) { Start-Sleep -Seconds 1 }
}
finally {
    $watcher.Dispose()
    $timer.Dispose()
}

 }).Trim()
        if([string]::IsNullOrWhiteSpace($msg)){
            $msg = '[코덱스] 자동 커밋: 변경사항 반영'
        } elseif(-not $msg.StartsWith('[코덱스]')){
            $msg = "[코덱스] $msg"
        }        git commit -m $msg | Out-Null
        Write-Host "자동 커밋 완료: $msg" -ForegroundColor Green
    }
    catch {
        Write-Err $_.Exception.Message
    }
})

$onChange = {
    param($sender, $eventArgs)
    try {
        $fullPath = $eventArgs.FullPath
        if(Test-Excluded $fullPath){ return }
        $pending = $true
        $null = $timer.Stop()
        $null = $timer.Start()
    } catch { Write-Err $_.Exception.Message }
}

$watcher.add_Changed($onChange)
$watcher.add_Created($onChange)
$watcher.add_Deleted($onChange)
$watcher.add_Renamed($onChange)

Write-Host "AutoCommit watcher 시작 (repo: $repoRoot, debounce: ${DebounceSeconds}s)" -ForegroundColor Cyan
Write-Host "종료: Ctrl + C"

try {
    while ($true) { Start-Sleep -Seconds 1 }
}
finally {
    $watcher.Dispose()
    $timer.Dispose()
}

