# Build CompatShield (Release) and the shieldtest harness (tests\shieldtest), then run every harness mode and total the results.
# The harness lives under this project (a short path), so it runs in place: no MAX_PATH copy needed.
$ErrorActionPreference = 'Continue'
$shield = Split-Path -Parent $PSScriptRoot
$harness = "$shield\tests\shieldtest"
$exe = "$harness\bin\Debug\net472\shieldtest.exe"

"=== build CompatShield (Release)"
$b1 = & dotnet build "$shield\CompatShield.csproj" -c Release -nologo -v q
$b1 | Select-String -Pattern 'error|warning CS|Build succeeded' | Select-Object -First 25 | ForEach-Object { $_.Line.Trim() }
if ($LASTEXITCODE -ne 0) { "BUILD FAILED (CompatShield)"; exit 1 }
Get-Item "$shield\bin\Release\netstandard2.1\CompatShield.dll" | Select-Object Length, LastWriteTime | Format-Table -HideTableHeaders

"=== build harness"
$b2 = & dotnet build "$harness\shieldtest.csproj" -nologo -v q
$b2 | Select-String -Pattern 'error|Build succeeded' | Select-Object -First 25 | ForEach-Object { $_.Line.Trim() }
if ($LASTEXITCODE -ne 0) { "BUILD FAILED (harness)"; exit 1 }
if ($exe.Length -gt 200) { "WARNING: harness path is $($exe.Length) chars; .NET Framework needs it under 260 with its files" }

$env:SHIELDTEST_DATA = "$shield\tests\shielddata"
$modes = @('', 'churn', 'respawn', 'deathtimer', 'lunarpool', 'shaman', 'tracer', 'lag', 'physcap', 'pileup', 'lockeditems', 'bighit', 'hooktimer', 'statlimit', 'hardcap', 'hallowed', 'cfgsave', 'totem', 'modguards', 'anchorguard', 'chain', 'blastcap', 'blastguard', 'statsguard', 'overlapguard', 'dotguard', 'bulletguard', 'acolyte', 'roulette', 'bubbets', 'lineardamage', 'skillslots', 'dagger', 'theta', 'nulldccs', 'genetics', 'configmove', 'wiring', 'inferno', 'swift', 'guardil', 'pti', 'flower', 'scepter', 'soh', 'sohcall', 'sacrifice', 'sackeep', 'sharesuite', 'zet')
$pass = 0; $fail = 0; $skip = 0
foreach ($m in $modes) {
  if ($m) { $env:SHIELDTEST_MODE = $m } else { Remove-Item Env:SHIELDTEST_MODE -ErrorAction SilentlyContinue }
  $out = & $exe | ForEach-Object { "$_" }
  $rc = $LASTEXITCODE
  $p = ($out | Select-String '^PASS').Count; $f = ($out | Select-String '^FAIL').Count; $s = ($out | Select-String '^SKIP').Count
  if ($rc -ne 0 -and $f -eq 0) { $f = 1; "    CRASHED (exit $rc): counted as a failure" }   # an unhandled exception must not hide behind 0 FAIL lines
  $pass += $p; $fail += $f; $skip += $s
  "--- mode '$m': $p pass, $f fail, $s skip (exit $rc)"
  if ($f -gt 0 -or $args -contains '-verbose') { $out | ForEach-Object { "    $_" } }
}
# The lag tracer's from-disk page-fault read uses a 64-bit Windows layout (the game is 64-bit); shieldtest is 32-bit,
# so the real read runs here in 64-bit PowerShell against the same net472 build.
$ps64 = if ([Environment]::Is64BitProcess) { "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" } else { "$env:WINDIR\sysnative\WindowsPowerShell\v1.0\powershell.exe" }
$out = & $ps64 -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\memreader_x64.ps1" | ForEach-Object { "$_" }
$rc = $LASTEXITCODE
$p = ($out | Select-String '^PASS').Count; $f = ($out | Select-String '^FAIL').Count; $s = ($out | Select-String '^SKIP').Count
if ($rc -ne 0 -and $f -eq 0) { $f = 1; "    CRASHED (exit $rc): counted as a failure" }
$pass += $p; $fail += $f; $skip += $s
"--- mode 'memreader64' (64-bit PowerShell): $p pass, $f fail, $s skip (exit $rc)"
if ($f -gt 0 -or $args -contains '-verbose') { $out | ForEach-Object { "    $_" } }
"=== TOTAL: $pass pass, $fail fail, $skip skip"
if ($fail -gt 0) { exit 1 }
