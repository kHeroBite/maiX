Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$proc = Get-Process -Name "mAIx" -ErrorAction SilentlyContinue
if (-not $proc) { Write-Host "NO_PROCESS"; exit 1 }

$cond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$app = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $app) { Write-Host "NO_WINDOW"; exit 1 }

# Find all hyperlink/listitem type controls (for sub-menu navigation)
$allConds = [System.Windows.Automation.AndCondition]::new(
    [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem),
    [System.Windows.Automation.NotCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::IsOffscreenProperty, $true)))

$items = $app.FindAll([System.Windows.Automation.TreeScope]::Descendants, $allConds)
Write-Host "LISTITEM_COUNT=$($items.Count)"
foreach ($item in $items) {
    $name = $item.Current.Name
    $automId = $item.Current.AutomationId
    Write-Host "ITEM|$automId|$name"
}
Write-Host "DONE"
