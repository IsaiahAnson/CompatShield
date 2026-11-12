# Runs CompatShield's real "page faults read from disk" reader (MemReader, used by the lag tracer since 1.4.54) in a
# 64-bit process, like the game. The shieldtest harness is a 32-bit process, where that read refuses on purpose, so
# run_harness.ps1 runs this script as its own mode. Prints PASS / FAIL / SKIP lines in the harness format.
# Takes about 10 s: it waits out the 6 s "too old to use" limit once.
param([string]$Dll)
$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitProcess) { "SKIP  memreader64: this PowerShell is a 32-bit process, so the real from-disk read was not run"; exit 0 }
if (-not $Dll) { $Dll = Join-Path (Split-Path -Parent $PSScriptRoot) 'bin\Release\net472\CompatShield.dll' }
if (-not (Test-Path $Dll)) { "FAIL  memreader64: no build at $Dll"; exit 0 }

function Check([string]$name, [bool]$ok) { if ($ok) { "PASS  $name" } else { "FAIL  $name" } }

# Loaded from bytes so the build output is never locked by this process.
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($Dll))
$reader = $asm.GetType('CompatShield.MemReader', $true)
$flags = [Reflection.BindingFlags]'Static,Public,NonPublic'
function Invoke-Reader([string]$name) { $reader.GetMethod($name, $flags).Invoke($null, $null) }
function Get-Worker { $reader.GetField('worker', $flags).GetValue($null) }
function Wait-Fresh([int]$limitMs) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $reading = Invoke-Reader 'Read'
    while (-not $reading.HardOk -and $clock.ElapsedMilliseconds -lt $limitMs) { [Threading.Thread]::Sleep(20); $reading = Invoke-Reader 'Read' }
    [pscustomobject]@{ Reading = $reading; Ms = $clock.ElapsedMilliseconds }
}

# 1. The direct read, 20 times: it must find this process each time and never exceed the total fault count.
$goodRounds = 0; $worstMs = 0.0; $hardNow = -1; $faultsNow = -1
for ($round = 0; $round -lt 20; $round++) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $hardNow = [long](Invoke-Reader 'HardNowForTest')
    $tookMs = $clock.Elapsed.TotalMilliseconds
    if ($round -gt 0 -and $tookMs -gt $worstMs) { $worstMs = $tookMs }   # round 0 includes compile time
    $faultsNow = [long](Invoke-Reader 'Faults')
    if ($hardNow -ge 0 -and $hardNow -le $faultsNow) { $goodRounds++ }
}
"  real 64-bit: this process has $hardNow page faults read from disk out of $faultsNow in all; the process-list read took up to $($worstMs.ToString('0.0')) ms"
Check "lag memory real 64-bit: the process list names this process and its from-disk count is no bigger than all its page faults, 20 times in a row" ($goodRounds -eq 20)

# 2. The background thread delivers a fresh value after a run start.
Invoke-Reader 'RunStarted' | Out-Null
$thread = Get-Worker
$first = Wait-Fresh 10000
"  real 64-bit: the background reader delivered a from-disk count of $($first.Reading.Hard) after $($first.Ms) ms"
Check "lag memory real 64-bit: after a run start the background thread (a named background thread below normal priority) delivers a fresh from-disk count" (
    $first.Reading.HardOk -and $first.Reading.Ok -and $first.Reading.DiskOk -and $thread -ne $null -and $thread.IsBackground -and
    $thread.Name -eq 'CompatShield.MemReader' -and $thread.Priority -eq [Threading.ThreadPriority]::BelowNormal -and
    [long]$first.Reading.Hard -le [long]$first.Reading.Faults)

Invoke-Reader 'RunStarted' | Out-Null
Check "lag memory real 64-bit: a second run start reuses the same thread" ([object]::ReferenceEquals($thread, (Get-Worker)) -and $thread.IsAlive)

# 3. After the run ends the thread stops sampling, so the last value goes stale and is not used.
Invoke-Reader 'RunEnded' | Out-Null
[Threading.Thread]::Sleep(7000)
$stale = Invoke-Reader 'Read'
Check "lag memory real 64-bit: 7 s after the run ended the old from-disk count is not used, while the cheap readings still work" ($stale.Ok -and -not $stale.HardOk -and $thread.IsAlive)

# 4. The next run start wakes the thread for a fresh value without waiting for its 2 s tick.
Invoke-Reader 'RunStarted' | Out-Null
$again = Wait-Fresh 5000
"  real 64-bit: the next run start got a fresh count after $($again.Ms) ms"
Check "lag memory real 64-bit: the next run start wakes the same thread and a fresh count arrives in under 1.5 s" (
    $again.Reading.HardOk -and $again.Ms -lt 1500 -and [object]::ReferenceEquals($thread, (Get-Worker)))
Invoke-Reader 'RunEnded' | Out-Null
$problem = Invoke-Reader 'TakeProblem'
if ($problem) { "  real 64-bit: problem reported: $problem" }
Check "lag memory real 64-bit: nothing went wrong, so there is no problem to log" ($null -eq $problem)
