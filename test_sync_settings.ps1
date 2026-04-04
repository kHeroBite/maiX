Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement

$proc = Get-Process -Name "mAIx" -ErrorAction SilentlyContinue
if (-not $proc) { Write-Host "NO_PROCESS"; exit 1 }
Write-Host "PID=$($proc.Id)"

$cond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$app = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $app) { Write-Host "NO_WINDOW"; exit 1 }
Write-Host "APP_FOUND"

$rbCond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::RadioButton)

$radioButtons = $app.FindAll([System.Windows.Automation.TreeScope]::Descendants, $rbCond)
Write-Host "RADIOBUTTON_COUNT=$($radioButtons.Count)"
foreach ($rb in $radioButtons) {
    try {
        $selPat = $rb.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $isSelected = $selPat.Current.IsSelected
        $name = $rb.Current.Name
        $automId = $rb.Current.AutomationId
        Write-Host "RB|$automId|$name|$isSelected"
    } catch {
        Write-Host "RB_ERR|$($rb.Current.AutomationId)|$($rb.Current.Name)"
    }
}

Write-Host "BUTTONS:"
$btnCond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Button)
$buttons = $app.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
foreach ($btn in $buttons) {
    $name = $btn.Current.Name
    $automId = $btn.Current.AutomationId
    if ($name -ne "" -or $automId -ne "") {
        Write-Host "BTN|$automId|$name"
    }
}
Write-Host "DONE"
