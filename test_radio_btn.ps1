Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement

$proc = Get-Process -Name "mAIx" -ErrorAction SilentlyContinue
if (-not $proc) { 
    Write-Host "FAIL: mAIx process not running"
    exit 1
}

Write-Host "Process found: PID $($proc.Id)"

$cond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$app = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)

if (-not $app) {
    Write-Host "FAIL: Cannot find app window via UIAutomation"
    exit 1
}

Write-Host "App window: $($app.Current.Name)"

$settingCond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "NavSettings")
$settingBtn = $app.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $settingCond)

if (-not $settingBtn) {
    $navItems = $app.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem))
    foreach ($item in $navItems) {
        Write-Host "  NavItem: Name='$($item.Current.Name)' Id='$($item.Current.AutomationId)'"
    }
}

if ($settingBtn) {
    Write-Host "Settings button found: $($settingBtn.Current.Name)"
    try {
        $inv = $settingBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $inv.Invoke()
        Write-Host "Clicked settings"
        Start-Sleep -Milliseconds 1500
    } catch {
        Write-Host "Click failed: $_"
    }
}

Write-Host "--- Scanning RadioButtons ---"
$radioCond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::RadioButton)
$radios = $app.FindAll([System.Windows.Automation.TreeScope]::Descendants, $radioCond)
Write-Host "Total RadioButtons: $($radios.Count)"
foreach ($r in $radios) {
    $isSelected = "N/A"
    try {
        $selPat = $r.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $isSelected = $selPat.Current.IsSelected
    } catch {}
    Write-Host "  Radio: Name='$($r.Current.Name)' Id='$($r.Current.AutomationId)' Selected=$isSelected Enabled=$($r.Current.IsEnabled)"
}

Write-Host "--- Done ---"
exit 0
