Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$proc = Get-Process -Name "mAIx" -ErrorAction SilentlyContinue
if (-not $proc) { Write-Host "NO_PROCESS"; exit 1 }

$cond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$app = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $app) { Write-Host "NO_WINDOW"; exit 1 }

# Name 기반으로 캘린더, 채팅 찾기
$types = @(
    [System.Windows.Automation.ControlType]::Button,
    [System.Windows.Automation.ControlType]::Text,
    [System.Windows.Automation.ControlType]::Hyperlink,
    [System.Windows.Automation.ControlType]::ListItem,
    [System.Windows.Automation.ControlType]::TabItem,
    [System.Windows.Automation.ControlType]::MenuItem,
    [System.Windows.Automation.ControlType]::Custom
)

foreach ($ct in $types) {
    $ctCond = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ct)
    $elems = $app.FindAll([System.Windows.Automation.TreeScope]::Descendants, $ctCond)
    foreach ($e in $elems) {
        $name = $e.Current.Name
        $automId = $e.Current.AutomationId
        if ($name -match "캘린더|달력|Calendar|채팅|Chat|메일|Mail|AI|분석") {
            Write-Host "FOUND|$($ct.ProgrammaticName)|$automId|$name|offscreen=$($e.Current.IsOffscreen)"
        }
    }
}
Write-Host "DONE"
