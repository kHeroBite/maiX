Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$proc = Get-Process -Name "mAIx" -ErrorAction SilentlyContinue
if (-not $proc) { Write-Host "NO_PROCESS"; exit 1 }

$cond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$app = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $app) { Write-Host "NO_WINDOW"; exit 1 }

# Dump ALL controls with AutomationId (not empty)
$allCond = [System.Windows.Automation.Condition]::TrueCondition
$all = $app.FindAll([System.Windows.Automation.TreeScope]::Descendants, $allCond)
Write-Host "TOTAL=$($all.Count)"
foreach ($e in $all) {
    $automId = $e.Current.AutomationId
    if ($automId -ne "" -and -not $e.Current.IsOffscreen) {
        $ctType = $e.Current.ControlType.ProgrammaticName
        $name = $e.Current.Name
        Write-Host "E|$automId|$ctType|$name"
    }
}
Write-Host "DONE"
