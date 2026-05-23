Set WshShell = CreateObject("WScript.Shell")
WshShell.AppActivate "mAIx"
WScript.Sleep 800
WshShell.SetCursorPos 120, 160
WScript.Sleep 300
' API 관리 클릭 (Y=160 부근)
Dim x, y
x = 120
y = 160
WshShell.SendClick x, y
WScript.Sleep 500
