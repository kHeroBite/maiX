<#
UTF-8 한글 훼손 방지 훅
- 스테이지된 텍스트 파일이 UTF-8로 인코딩되었는지 검증합니다.
- 잘못된 UTF-8 시퀀스, 대체문자(�), 흔한 모지바케 패턴을 탐지하면 커밋을 차단합니다.
- 우회가 필요한 경우 환경변수 SKIP_UTF8_GUARD=1 설정 후 커밋하세요.
#>

[Console]::InputEncoding = [Text.UTF8Encoding]::new()
[Console]::OutputEncoding = [Text.UTF8Encoding]::new()

Param()

if ($env:SKIP_UTF8_GUARD -eq '1') { exit 0 }

$ErrorActionPreference = 'Stop'

function Get-StagedFiles {
  $names = git diff --cached --name-only --diff-filter=ACM
  if ($LASTEXITCODE -ne 0) { return @() }
  return $names | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
}

function Test-IsTextTarget($path) {
  # 핵심 소스/리소스만 검사 (스크립트/훅 디렉터리는 제외)
  if ($path -like '.githooks/*' -or $path -like '.git/*') { return $false }
  $ext = [IO.Path]::GetExtension($path).ToLowerInvariant()
  return @('.cs','.csproj','.sln','.config','.resx','.xml','.json','.md','.yml','.yaml','.txt','.csx') -contains $ext
}

function Test-Utf8BytesStrict($bytes) {
  $utf8 = [System.Text.UTF8Encoding]::new($false, $true) # no BOM, throwOnInvalidBytes
  try { [void]$utf8.GetString($bytes); return $true } catch { return $false }
}

function Test-HasMojibake($text) {
  if (-not $text) { return $false }
  # U+FFFD replacement char or '?' 앞뒤로 한글이 섞인 패턴, 흔한 깨짐 글자군 포함
  $patterns = @(
    '\uFFFD',           # replacement char
    '\?[가-힣]',         # ?가, ?한 등
    '[가-힣]\?',         # 가? 한?
    '[遺꾨앺듃쟻怨湲뺣낫媛꾪諛꽭궗蹂냩쐞]', # 흔한 깨짐 조합 일부
    '�'                   # console 상 표현
  )
  foreach ($p in $patterns) {
    if ($text -match $p) { return $true }
  }
  return $false
}

$failures = @()

foreach ($file in Get-StagedFiles) {
  if (-not (Test-IsTextTarget $file)) { continue }
  $bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $file))

  # BOM 검사 (UTF-16/UTF-32 등 금지)
  if ($bytes.Length -ge 2) {
    $bom16le = ($bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE)
    $bom16be = ($bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF)
    $bom32le = ($bytes.Length -ge 4 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE -and $bytes[2] -eq 0x00 -and $bytes[3] -eq 0x00)
    $bom32be = ($bytes.Length -ge 4 -and $bytes[0] -eq 0x00 -and $bytes[1] -eq 0x00 -and $bytes[2] -eq 0xFE -and $bytes[3] -eq 0xFF)
    if ($bom16le -or $bom16be -or $bom32le -or $bom32be) {
      $failures += "[BOM] UTF-16/32 BOM 금지: $file"
      continue
    }
  }

  # 엄격 UTF-8 검증
  if (-not (Test-Utf8BytesStrict $bytes)) {
    $failures += "[ENCODING] 유효하지 않은 UTF-8 바이트 시퀀스: $file"
    continue
  }

  # 모지바케 탐지
  $text = [Text.Encoding]::UTF8.GetString($bytes)
  if (Test-HasMojibake $text) {
    $failures += "[MOJIBAKE] 한글 깨짐 의심 패턴 발견: $file"
  }
}

if ($failures.Count -gt 0) {
  Write-Host "한글/UTF-8 검사 실패로 커밋이 중단되었습니다:" -ForegroundColor Red
  $failures | ForEach-Object { Write-Host " - $_" -ForegroundColor Yellow }
  Write-Host "
해결 방법:
  1) 파일을 UTF-8(무 BOM)로 다시 저장
  2) .editorconfig 준수 확인 (charset = utf-8)
  3) PowerShell/VSCode 터미널을 UTF-8로 설정 (chcp 65001)
  4) 정말 우회 필요 시, 임시로 SKIP_UTF8_GUARD=1 환경변수로 우회 가능
" -ForegroundColor Cyan
  exit 1
}

exit 0
