# Waits for Risk of Rain 2 to close, then installs the built CompatShield DLL into the Default profile, backing up the
# installed one first. Refuses if CompatShield.cs is newer than the build. Run it in the background right after a build.
#   powershell -File install-on-close.ps1 [-Label 1.4.3]
param([string]$Label = "")
$shield = Split-Path -Parent $PSScriptRoot
$src = "$shield\bin\Release\netstandard2.1\CompatShield.dll"
$srcCs = "$shield\CompatShield.cs"
$dst = "$env:APPDATA\Thunderstore Mod Manager\DataFolder\RiskOfRain2\profiles\Default\BepInEx\patchers\Local-CompatShield\CompatShield.dll"
"waiting for Risk of Rain 2 to close (started $(Get-Date -Format 'HH:mm:ss'))..."
while (Get-Process "Risk of Rain 2" -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 2 }
"game closed at $(Get-Date -Format 'HH:mm:ss')"
Start-Sleep -Seconds 4
$bak = "$shield\tools\backups\dll-" + (Get-Date -Format "yyyyMMdd-HHmmss")
New-Item -ItemType Directory -Force $bak | Out-Null
if (Test-Path $dst) { Copy-Item $dst (Join-Path $bak "CompatShield.dll.installed") -ErrorAction Stop }
if ((Get-Item $srcCs).LastWriteTimeUtc -gt (Get-Item $src).LastWriteTimeUtc) {
  "CompatShield.cs is newer than the built DLL: rebuild first. DLL NOT installed."
  exit 1
}
Copy-Item $src $dst -Force -ErrorAction Stop
"CompatShield $Label installed: $((Get-Item $dst).Length) bytes, matches build: $((Get-FileHash $dst).Hash -eq (Get-FileHash $src).Hash)"
"backup: $bak"
