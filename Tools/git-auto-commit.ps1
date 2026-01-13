# Console encoding (UTF-8)
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# Find repository root
function Get-GitRoot {
  $p = (git rev-parse --show-toplevel 2>$null)
  if ([string]::IsNullOrWhiteSpace($p)) { return (Get-Location).Path }
  return $p.Trim()
}

$repoRoot = Get-GitRoot
Set-Location $repoRoot

# Load settings (best-effort)
$configPath = Join-Path $repoRoot ".codex/config.yaml"
$autoCommit = $true
$defaultMsg = "Auto commit: apply changes"
$msgFileName = ".commit_message.txt"

if (Test-Path $configPath) {
  $yaml = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8
  # Minimalistic key:value parser (avoid external modules)
  foreach ($line in $yaml -split "`n") {
    $trim = $line.Trim()
    if ($trim -like "#*") { continue }
    if ($trim -match "^(?<k>[^:]+):\s*(?<v>.+)$") {
      $k = $Matches.k.Trim()
      $v = $Matches.v.Trim()
      # strip inline comments after value
      $v = ($v -replace '\s+#.*$','').Trim().Trim('"')
      switch -Regex ($k) {
        '^autoCommit$' { $autoCommit = ($v -match '^(?i:true|1|yes)$'); break }
        '^defaultCommitMessage$' { $defaultMsg = $v; break }
        '^messageFile$' { $msgFileName = $v; break }
      }
    }
  }
}

if (-not $autoCommit) { Write-Host "Auto-commit disabled by config."; exit 0 }

# Stage changes
git add -A | Out-Null

# Check staged changes
git diff --cached --quiet
if ($LASTEXITCODE -eq 0) {
  Write-Host "No changes to commit"
  exit 0
}

# Decide commit message
$msgFile = Join-Path $repoRoot $msgFileName
if (Test-Path $msgFile) {
  $content = (Get-Content -LiteralPath $msgFile -Raw -Encoding UTF8).Trim()
} else {
  $content = ""
}

if ([string]::IsNullOrWhiteSpace($content)) { $content = $defaultMsg }

# Commit
git commit -m $content
if ($LASTEXITCODE -ne 0) {
  Write-Host "Commit failed"; exit $LASTEXITCODE
}

Write-Host "Auto-commit complete: $content"
