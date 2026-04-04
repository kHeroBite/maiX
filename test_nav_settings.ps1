Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$proc = Get-Process -Name "mAIx" -ErrorAction SilentlyContinue
if (-not $proc) { Write-Host "NO_PROCESS"; exit 1 }

$cond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$app = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $app) { Write-Host "NO_WINDOW"; exit 1 }

# NavSettingsButton 클릭
$settCond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "NavSettingsButton")
$settBtn = $app.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $settCond)
if (-not $settBtn) { Write-Host "NO_SETTINGS_BTN"; exit 1 }

$selPat = $settBtn.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
$selPat.Select()
Write-Host "SETTINGS_CLICKED"
Start-Sleep -Milliseconds 1000

# 현재 화면 RadioButton 목록
$rbCond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::RadioButton)
$radioButtons = $app.FindAll([System.Windows.Automation.TreeScope]::Descendants, $rbCond)
Write-Host "RADIOBUTTON_COUNT=$($radioButtons.Count)"
foreach ($rb in $radioButtons) {
    try {
        $selPatRB = $rb.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $isSelected = $selPatRB.Current.IsSelected
        $name = $rb.Current.Name
        $automId = $rb.Current.AutomationId
        Write-Host "RB|$automId|$name|$isSelected"
    } catch {
        Write-Host "RB_ERR|$($rb.Current.AutomationId)|$($rb.Current.Name)"
    }
}
Write-Host "DONE"
