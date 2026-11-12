# Waits for Risk of Rain 2 to close, backs up two configs, then applies: AsyncLoggers writes the disk log synchronously
# (a crash can no longer lose the last seconds of LogOutput.log) and Multitudes logs its bans.
$cfg = "$env:APPDATA\Thunderstore Mod Manager\DataFolder\RiskOfRain2\profiles\Default\BepInEx\config"
"waiting for Risk of Rain 2 to close (started $(Get-Date -Format HH:mm:ss))..."
while (Get-Process -Name 'Risk of Rain 2' -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 5 }
"game closed at $(Get-Date -Format HH:mm:ss)"
$bak = "C:\Users\isaia\OneDrive\Desktop\RoR2 Mod Watcher\ror2-compat-shield\tools\backups\cfg-" + (Get-Date -Format "yyyyMMdd-HHmmss")
New-Item -ItemType Directory -Force $bak | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding $false
$edits = @(
  @{ file = "$cfg\AsyncLoggers\Config.cfg"; pattern = '(?m)^Async File = true(?=\r?$)'; value = 'Async File = false' },
  @{ file = "$cfg\com.Snoresville.MultitudesNoPriceScale.cfg"; pattern = '(?m)^Log Bans = false(?=\r?$)'; value = 'Log Bans = true' }
)
foreach ($e in $edits) {
  if (-not (Test-Path $e.file)) { "missing: $($e.file)"; continue }
  Copy-Item $e.file (Join-Path $bak (Split-Path $e.file -Leaf))
  $text = [IO.File]::ReadAllText($e.file)
  $new = [regex]::Replace($text, $e.pattern, $e.value)
  if ($new -eq $text) { "no change (already set or key differs): $($e.file)" } else { [IO.File]::WriteAllText($e.file, $new, $utf8); "applied: $($e.value) in $(Split-Path $e.file -Leaf)" }
}
"backup: $bak"