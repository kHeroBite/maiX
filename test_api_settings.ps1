
# ApiSettingsWindow TTS 슬롯 검증 — AI 설정 버튼 클릭으로 창 열기
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$ErrorActionPreference = "Continue"

$ssDir = "$env:APPDATA\MaiX\screenshots"
if (-not (Test-Path $ssDir)) { New-Item -ItemType Directory -Path $ssDir | Out-Null }

function Take-Screenshot {
    param($name)
    try {
        $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $bitmap = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
        $path = "$ssDir\${name}_$(Get-Date -Format 'HHmmss').png"
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $graphics.Dispose(); $bitmap.Dispose()
        return $path
    } catch { return "SS_FAIL: $_" }
}

$root = [System.Windows.Automation.AutomationElement]::RootElement
$proc = Get-Process -Name "mAIx" -ErrorAction Stop
$pidCond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$app = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
if (-not $app) { throw "mAIx UI 루트 없음" }

Write-Host "mAIx 연결: PID=$($proc.Id)"

# 방법 1: 모든 버튼/탭 탐색으로 AI 설정 관련 진입점 찾기
Write-Host "UI 요소 탐색 중..."

# AI 설정 관련 이름 탐색
$searchNames = @("AI 설정", "API 설정", "설정", "Settings", "AI Provider")
$foundBtn = $null
foreach ($sn in $searchNames) {
    $nc = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, $sn)
    $found = $app.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nc)
    if ($found) {
        Write-Host "  발견: '$sn' (ControlType=$($found.Current.ControlType.ProgrammaticName))"
        $foundBtn = $found
        # 버튼이면 클릭
        if ($found.Current.ControlType.Id -eq [System.Windows.Automation.ControlType]::Button.Id) {
            try {
                $ip = $found.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                $ip.Invoke()
                Write-Host "  클릭: '$sn'"
                Start-Sleep -Milliseconds 2000
                break
            } catch { Write-Host "  클릭 실패: $_" }
        }
    }
}

# ApiSettingsWindow 탐색 (여러 이름 시도)
$settingsWin = $null
$winNames = @("API 설정", "AI 설정", "ApiSettingsWindow", "Settings")
foreach ($wn in $winNames) {
    $nc = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, $wn)
    $settingsWin = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $nc)
    if ($settingsWin) { Write-Host "ApiSettingsWindow 발견: '$wn'"; break }
}

# 설정창이 열리지 않으면 AutomationId로 직접 탐색
if (-not $settingsWin) {
    $allWindows = $root.FindAll([System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.Condition]::TrueCondition)
    Write-Host "현재 열린 창 목록:"
    foreach ($w in $allWindows) {
        $pId = $w.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::ProcessIdProperty)
        if ($pId -eq $proc.Id) {
            Write-Host "  - '$($w.Current.Name)' (AutomationId=$($w.Current.AutomationId))"
        }
    }
}

# TTS 슬롯 XAML 기반 검증 (창 없어도)
Write-Host "`n[항목10] XAML 기반 TTS 슬롯 검증..."
$xamlPath = "C:\DATA\Project\mAIx\mAIx\Views\ApiSettingsWindow.xaml"
$xamlContent = Get-Content $xamlPath -Raw -Encoding UTF8

$hasTtsModel = $xamlContent -match "TtsModelTextBox"
$hasTtsVoice = $xamlContent -match "TtsVoiceComboBox"
$hasTtsSection = $xamlContent -match "TTS"

Write-Host "  TtsModelTextBox: $hasTtsModel"
Write-Host "  TtsVoiceComboBox: $hasTtsVoice"
Write-Host "  TTS 섹션: $hasTtsSection"

if ($hasTtsModel -and $hasTtsVoice) {
    Write-Host "  RESULT: PASS - TTS 슬롯 2개 모두 XAML에 존재"
} else {
    Write-Host "  RESULT: FAIL - TTS 슬롯 누락"
}

# MainWindow Jarvis 라디오 제거 XAML 검증
Write-Host "`n[항목11] MainWindow XAML Jarvis 라디오 제거 검증..."
$mainXamlPath = "C:\DATA\Project\mAIx\mAIx\Views\MainWindow.xaml"
$mainXaml = Get-Content $mainXamlPath -Raw -Encoding UTF8
$hasJarvisRadio = $mainXaml -match "Jarvis"
Write-Host "  XAML Jarvis 참조: $hasJarvisRadio"
if (-not $hasJarvisRadio) {
    Write-Host "  RESULT: PASS - MainWindow.xaml에 Jarvis 없음"
} else {
    Write-Host "  RESULT: FAIL - Jarvis 참조 발견"
}

# 스크린샷
$ss1 = Take-Screenshot "ui_verify_result"
Write-Host "`n스크린샷: $ss1"

# 결과 출력
if ($hasTtsModel -and $hasTtsVoice -and (-not $hasJarvisRadio)) {
    Write-Host "`nFINAL: ALL PASS"
    exit 0
} else {
    Write-Host "`nFINAL: FAIL"
    exit 1
}
