Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$proc = Get-Process -Name "mAIx" -ErrorAction SilentlyContinue
if (-not $proc) { Write-Host "NO_PROCESS"; exit 1 }

$cond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$app = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $app) { Write-Host "NO_WINDOW"; exit 1 }

# Find all non-offscreen controls without AutomationId filter
$allCond = [System.Windows.Automation.Condition]::TrueCondition
$all = $app.FindAll([System.Windows.Automation.TreeScope]::Descendants, $allCond)
Write-Host "TOTAL_ALL=$($all.Count)"

# Show controls near the sync submenu area
foreach ($e in $all) {
    $ctType = $e.Current.ControlType.ProgrammaticName
    $name = $e.Current.Name
    $automId = $e.Current.AutomationId
    $offscreen = $e.Current.IsOffscreen
    if ($ctType -eq "ControlType.Button" -and -not $offscreen) {
        Write-Host "BTN|$automId|$name|offscreen=$offscreen"
    }
    if ($ctType -eq "ControlType.Text" -and -not $offscreen) {
        Write-Host "TXT|$automId|$name"
    }
}
Write-Host "DONE"
