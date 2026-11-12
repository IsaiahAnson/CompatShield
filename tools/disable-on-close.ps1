# Waits for Risk of Rain 2 to close, then runs disable_mod.py outside the Claude package (WMI starts it, so its writes reach the
# real AppData instead of the package's LocalCache). The result is the disable-<mod>-<time>.txt report in tools\backups.
#   powershell -File disable-on-close.ps1 -Mod Author-ModName
param([Parameter(Mandatory = $true)][string]$Mod)
$python = "C:\Users\isaia\AppData\Local\Python\bin\python.exe"
$script = Join-Path $PSScriptRoot "disable_mod.py"
"waiting for Risk of Rain 2 to close (started $(Get-Date -Format 'HH:mm:ss'))..."
while (Get-Process "Risk of Rain 2" -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 2 }
"game closed at $(Get-Date -Format 'HH:mm:ss')"
Start-Sleep -Seconds 6
$line = "`"$python`" -I `"$script`" $Mod"
$r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $line }
"started outside the package: pid $($r.ProcessId), return $($r.ReturnValue)"
