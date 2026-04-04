Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$proc = Get-Process -Name "mAIx" -ErrorAction SilentlyContinue
if (-not $proc) { Write-Host "NO_PROCESS"; exit 1 }

$cond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$app = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $app) { Write-Host "NO_WINDOW"; exit 1 }

# Get bounding rectangles of all elements
$allCond = [System.Windows.Automation.Condition]::TrueCondition
$all = $app.FindAll([System.Windows.Automation.TreeScope]::Descendants, $allCond)

foreach ($e in $all) {
    $name = $e.Current.Name
    $ctType = $e.Current.ControlType.ProgrammaticName
    $rect = $e.Current.BoundingRectangle
    $offscreen = $e.Current.IsOffscreen
    # Only show tab-like elements
    if (-not $offscreen -and ($name -eq "Calendar" -or $name -eq "Chat" -or $ctType -eq "ControlType.TabItem")) {
        Write-Host "TAB|$ctType|$name|X=$($rect.X) Y=$($rect.Y) W=$($rect.Width) H=$($rect.Height)"
    }
}

# Try clicking using coordinate - find Calendar tab text position
foreach ($e in $all) {
    $name = $e.Current.Name
    $ctType = $e.Current.ControlType.ProgrammaticName
    $rect = $e.Current.BoundingRectangle
    $automId = $e.Current.AutomationId
    $offscreen = $e.Current.IsOffscreen
    if (-not $offscreen -and $ctType -eq "ControlType.Custom") {
        Write-Host "CUSTOM|$automId|$name|X=$($rect.X) Y=$($rect.Y) W=$($rect.Width) H=$($rect.Height)"
    }
}
Write-Host "DONE"
