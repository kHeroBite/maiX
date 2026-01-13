# UTF-8 肄섏넄 ?ㅼ젙
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new()
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()

$ErrorActionPreference = 'Stop'

function Get-GitRoot {
  $p = (git rev-parse --show-toplevel 2>$null)
  if ([string]::IsNullOrWhiteSpace($p)) { return (Get-Location).Path }
  return $p.Trim()
}

$root = Get-GitRoot
Set-Location $root

git config core.hooksPath .githooks
git config i18n.commitEncoding utf-8
git config i18n.logOutputEncoding utf-8

Write-Host "Git hooksPath='.githooks' 諛?i18n ?몄퐫???ㅼ젙 ?꾨즺"


# 권장 Git 인코딩/개행 설정
 git config core.autocrlf false | Out-Null
 git config core.eol crlf | Out-Null
 git config gui.encoding utf-8 | Out-Null
