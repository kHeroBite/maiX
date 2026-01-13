[Console]::InputEncoding = [Text.UTF8Encoding]::new()
[Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$ErrorActionPreference = 'SilentlyContinue'

function Test-WatcherRunning {
  try {
    $procs = Get-CimInstance Win32_Process -Filter "Name = 'powershell.exe' OR Name = 'pwsh.exe'"
    return $procs | Where-Object { $_.CommandLine -match 'Tools/AutoCommit/AutoCommit.ps1' } | ForEach-Object { $_ } | Measure-Object | Select-Object -ExpandProperty Count
  } catch { return 0 }
}

if ((Test-WatcherRunning) -eq 0) {
  try {
    $script = Join-Path (git rev-parse --show-toplevel) 'Tools/AutoCommit/AutoCommit.ps1'
    if (Test-Path $script) {
      Start-Process powershell.exe -ArgumentList "-NoProfile","-ExecutionPolicy","Bypass","-File",$script,"-DebounceSeconds",3 -WindowStyle Hidden | Out-Null
    }
  } catch {}
}

exit 0

