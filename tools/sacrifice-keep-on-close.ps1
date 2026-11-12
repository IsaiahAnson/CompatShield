# Once Risk of Rain 2 has closed: makes sure the newest CompatShield build is installed, then adds his Sacrifice interactable
# exceptions to the config. Waits for another on-close waiter (-OtherWaiter <PID>) so two scripts never edit the config at once.
# -TestConfig <path> skips the waiting and the install and edits that file instead, to try the edit on a copy.
param([int]$OtherWaiter = 0, [int]$OtherWaiterMaxSeconds = 90, [string]$TestConfig = "")
$ErrorActionPreference = "Stop"
$game = "Risk of Rain 2"
$shield = Split-Path -Parent $PSScriptRoot
$bepin = "$env:APPDATA\Thunderstore Mod Manager\DataFolder\RiskOfRain2\profiles\Default\BepInEx"
$cfg = if ($TestConfig) { $TestConfig } else { "$bepin\config\local.compatshield.cfg" }
$dll = "$bepin\patchers\Local-CompatShield\CompatShield.dll"
$build = "$shield\bin\Release\netstandard2.1\CompatShield.dll"

if (-not $TestConfig) {
    "waiting for the game to close (started $(Get-Date -Format HH:mm:ss))"
    do {
        while (Get-Process $game -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 5 }
        $waited = 0
        while ($OtherWaiter -and (Get-Process -Id $OtherWaiter -ErrorAction SilentlyContinue) -and $waited -lt $OtherWaiterMaxSeconds) {
            Start-Sleep -Seconds 3
            $waited += 3
        }
        Start-Sleep -Seconds 6
    } while (Get-Process $game -ErrorAction SilentlyContinue)
    "game closed at $(Get-Date -Format HH:mm:ss)"

    if ((Get-FileHash $dll).Hash -ne (Get-FileHash $build).Hash) {
        "the installed DLL differs from the newest build, installing it"
        & "$shield\tools\install-on-close.ps1" -Label newest-at-close
        if ((Get-FileHash $dll).Hash -ne (Get-FileHash $build).Hash) { "WARNING: the installed DLL still differs from the newest build" }
    }

    $bak = "$shield\tools\backups\cfg-backup-$(Get-Date -Format yyyyMMdd-HHmmss)-sacrifice"
    New-Item -ItemType Directory -Force $bak | Out-Null
    Copy-Item $cfg $bak
    "config backup: $bak"
}

$t = [IO.File]::ReadAllText($cfg)
$old = "iscShrineChance, iscCasinoChest, iscEquipmentBarrel, iscLunarChest"
$list = "$old, iscTripleShopEquipment, iscVoidChest"
$add = ""

# A value he set himself is left alone. An empty one (written by a launch in between) or the earlier four-name list gets the current list.
$upgrade = [regex]('(?m)^KeepInteractables[ \t]*=[ \t]*(' + [regex]::Escape($old) + ')?[ \t]*(?=\r?$)')
if ($upgrade.IsMatch($t)) {
    $t = $upgrade.Replace($t, "KeepInteractables = $list", 1)
} elseif ($t -notmatch "(?m)^KeepInteractables[ \t]*=") {
    $add += "`r`n## Interactables that still spawn under Artifact of Sacrifice.`r`n# Setting type: String`r`n# Default value: `r`nKeepInteractables = $list`r`n"
}
if ($t -notmatch "(?m)^KeptInteractableWeight[ \t]*=") {
    $add += "`r`n## How much of a spawn category's share goes to the kept cards.`r`n# Setting type: Single`r`n# Default value: 1`r`nKeptInteractableWeight = 0.5`r`n"
}
if ($add) {
    $rx = [regex]"(?m)^DropChanceMultiplier[ \t]*=[^\r\n]*\r?\n"
    if (-not $rx.IsMatch($t)) { throw "DropChanceMultiplier not found, config left alone" }
    $t = $rx.Replace($t, { param($m) $m.Value + $add }, 1)
}
[IO.File]::WriteAllText($cfg, $t, (New-Object Text.UTF8Encoding $false))
"done: " + ((Select-String -Path $cfg -Pattern "^(KeepInteractables|KeptInteractableWeight|DropChanceMultiplier)" | ForEach-Object Line) -join "; ")
