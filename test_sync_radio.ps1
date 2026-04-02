Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$proc = Get-Process -Name "mAIx" -ErrorAction SilentlyContinue
if (-not $proc) { Write-Host "FAIL: process not running"; exit 1 }

$procIdProp = [System.Windows.Automation.AutomationElement]::ProcessIdProperty
$autoIdProp = [System.Windows.Automation.AutomationElement]::AutomationIdProperty
$ctrlTypeProp = [System.Windows.Automation.AutomationElement]::ControlTypeProperty
$scope = [System.Windows.Automation.TreeScope]

$appCond = New-Object System.Windows.Automation.PropertyCondition($procIdProp, $proc.Id)
$app = $root.FindFirst($scope::Children, $appCond)
if (-not $app) { Write-Host "FAIL: app window not found"; exit 1 }
Write-Host "App: $($app.Current.Name)"

# NavSettingsButton 클릭
$navCond = New-Object System.Windows.Automation.PropertyCondition($autoIdProp, "NavSettingsButton")
$navBtn = $app.FindFirst($scope::Descendants, $navCond)
if ($navBtn) {
    try {
        $selPat = $navBtn.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $selPat.Select()
        Write-Host "Selected NavSettingsButton"
    } catch {
        Write-Host "WARN: $($_.Exception.Message)"
    }
    Start-Sleep -Milliseconds 2000
}

# 스크린샷 API 호출
$null = Invoke-WebRequest -Uri "http://localhost:5858/api/screenshot" -Method POST -UseBasicParsing -ErrorAction SilentlyContinue

# 라디오 버튼 전체 재스캔
$radioType = [System.Windows.Automation.ControlType]::RadioButton
$radioCond = New-Object System.Windows.Automation.PropertyCondition($ctrlTypeProp, $radioType)
$radios = $app.FindAll($scope::Descendants, $radioCond)
Write-Host "RadioButtons: $($radios.Count)"
foreach ($r in $radios) {
    $isSelected = "N/A"
    try {
        $sp = $r.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $isSelected = $sp.Current.IsSelected
    } catch {}
    Write-Host "  Radio: Name='$($r.Current.Name)' Id='$($r.Current.AutomationId)' Selected=$isSelected"
}

# TabItem 탐색
$tabType = [System.Windows.Automation.ControlType]::TabItem
$tabCond = New-Object System.Windows.Automation.PropertyCondition($ctrlTypeProp, $tabType)
$tabs = $app.FindAll($scope::Descendants, $tabCond)
Write-Host "TabItems: $($tabs.Count)"
foreach ($t in $tabs) {
    Write-Host "  Tab: Name='$($t.Current.Name)' Id='$($t.Current.AutomationId)'"
}

Write-Host "--- Done ---"
exit 0
