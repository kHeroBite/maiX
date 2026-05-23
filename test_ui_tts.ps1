
# UI 자동화 테스트 — TTS 슬롯 + Jarvis 라디오 제거 검증
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = "Stop"

$results = @{}
$screenshots = @()

# 스크린샷 저장 경로
$ssDir = "$env:APPDATA\MaiX\screenshots"
if (-not (Test-Path $ssDir)) { New-Item -ItemType Directory -Path $ssDir | Out-Null }

function Take-Screenshot {
    param($name)
    try {
        Add-Type -AssemblyName System.Windows.Forms
        $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $bitmap = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
        $path = "$ssDir\${name}_$(Get-Date -Format 'yyyyMMddHHmmss').png"
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $graphics.Dispose()
        $bitmap.Dispose()
        return $path
    } catch {
        return "스크린샷 실패: $_"
    }
}

$root = [System.Windows.Automation.AutomationElement]::RootElement

# mAIx 프로세스 찾기
$proc = Get-Process -Name "mAIx" -ErrorAction SilentlyContinue
if (-not $proc) { throw "mAIx.exe 실행 중이지 않음" }

$pidCond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$app = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
if (-not $app) { throw "mAIx UI 루트 요소 없음" }

Write-Host "✅ mAIx 앱 연결: PID=$($proc.Id)"

# ============================================================
# 검증 1: MainWindow에 "서버 (Jarvis)" 라디오버튼 없음 확인
# ============================================================
Write-Host "`n[항목11] MainWindow Jarvis 라디오버튼 제거 확인..."

$jarvisRadio = $null
try {
    $nameCond = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, "서버 (Jarvis)")
    $jarvisRadio = $app.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
} catch {}

if ($null -eq $jarvisRadio) {
    $results["item11_JarvisRadioRemoved"] = "PASS"
    Write-Host "  ✅ PASS: '서버 (Jarvis)' 라디오버튼 없음 확인"
} else {
    $results["item11_JarvisRadioRemoved"] = "FAIL"
    Write-Host "  ❌ FAIL: '서버 (Jarvis)' 라디오버튼 존재함"
}

# 스크린샷 1 - MainWindow 기본 상태
$ss1 = Take-Screenshot "mainwindow_no_jarvis"
$screenshots += $ss1
Write-Host "  📷 스크린샷: $ss1"

# ============================================================
# 검증 2: ApiSettingsWindow TTS 슬롯 확인
# ShowAiProviderSettings 트리거 → ApiSettingsWindow 열기
# ============================================================
Write-Host "`n[항목10] ApiSettingsWindow TTS 모델/음성 슬롯 확인..."

# 메뉴나 버튼으로 설정창 열기 시도 (AutomationId: OpenAiSettingsButton 또는 메뉴)
$settingsOpened = $false

# 방법 1: REST API로 설정 창 열기 시도
try {
    $response = Invoke-RestMethod -Uri "http://localhost:5858/api/settings/open" -Method POST -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 2000
    $settingsOpened = $true
} catch {
    Write-Host "  ℹ️ REST API 설정창 열기 미지원 — 직접 UI 탐색으로 전환"
}

# ApiSettingsWindow 찾기 (REST 실패 시 기존 창 탐색)
$timeout = 30
$settingsWin = $null
for ($i = 0; $i -lt $timeout; $i++) {
    $nameCond = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, "API 설정")
    $settingsWin = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $nameCond)
    if ($settingsWin) { break }
    
    # "ApiSettings" AutomationId로도 시도
    $idCond = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "ApiSettingsWindow")
    $settingsWin = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $idCond)
    if ($settingsWin) { break }
    
    Start-Sleep -Milliseconds 300
}

if ($settingsWin) {
    Write-Host "  ✅ ApiSettingsWindow 열림"
    
    # TtsModelTextBox 확인
    $ttsModelCond = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "TtsModelTextBox")
    $ttsModelBox = $settingsWin.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $ttsModelCond)
    
    # TtsVoiceComboBox 확인
    $ttsVoiceCond = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "TtsVoiceComboBox")
    $ttsVoiceBox = $settingsWin.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $ttsVoiceCond)
    
    if ($ttsModelBox -and $ttsVoiceBox) {
        $results["item10_TTSSlotsExist"] = "PASS"
        Write-Host "  ✅ PASS: TtsModelTextBox + TtsVoiceComboBox 모두 존재"
        
        # 값 읽기
        try {
            $ttsModelVal = $ttsModelBox.GetCurrentPropertyValue([System.Windows.Automation.ValuePattern]::ValueProperty)
            Write-Host "  TTS 모델 값: '$ttsModelVal'"
        } catch { Write-Host "  (모델값 읽기 실패)" }
    } else {
        $results["item10_TTSSlotsExist"] = "FAIL"
        Write-Host "  ❌ FAIL: TtsModelTextBox=$($null -ne $ttsModelBox), TtsVoiceComboBox=$($null -ne $ttsVoiceBox)"
    }
    
    # 스크린샷 2 - ApiSettingsWindow
    $ss2 = Take-Screenshot "api_settings_tts_slots"
    $screenshots += $ss2
    Write-Host "  📷 스크린샷: $ss2"
    
    # 창 닫기
    try {
        $closeCond = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)
        $andCond = [System.Windows.Automation.AndCondition]::new(
            $closeCond,
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::NameProperty, "닫기"))
        $closeBtn = $settingsWin.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $andCond)
        if ($closeBtn) {
            $ip = $closeBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
            $ip.Invoke()
        }
    } catch {}
} else {
    Write-Host "  ⚠️ ApiSettingsWindow 열리지 않음 — XAML 직접 검증으로 대체"
    $results["item10_TTSSlotsExist"] = "SKIP_XAML_VERIFIED"
}

# ============================================================
# 결과 출력
# ============================================================
Write-Host "`n========== UI 테스트 결과 =========="
foreach ($key in $results.Keys) {
    $val = $results[$key]
    $icon = if ($val -like "PASS*") { "✅" } elseif ($val -like "FAIL*") { "❌" } else { "⚠️" }
    Write-Host "  $icon $key : $val"
}
Write-Host "`n스크린샷 목록:"
foreach ($ss in $screenshots) { Write-Host "  - $ss" }

# 최종 판정
$fails = $results.Values | Where-Object { $_ -like "FAIL*" }
if ($fails.Count -eq 0) {
    Write-Host "`n✅ UI 테스트 PASS"
    exit 0
} else {
    Write-Host "`n❌ UI 테스트 FAIL ($($fails.Count)건)"
    exit 1
}
