<#
Safe integration test on a REAL, already-open PiP.
No fake browsers or dummy windows. Always restores the original window style,
even when an assertion fails.
#>
param([switch]$KeepCaptured)
$ErrorActionPreference = 'Stop'
$ctl = Join-Path $PSScriptRoot 'pipctl.ps1'
function Call([string]$cmd, [string]$arg = '') {
    $response = & $ctl $cmd $arg | Out-String
    if ($LASTEXITCODE -ne 0) { throw "$cmd failed: $response" }
    return $response | ConvertFrom-Json
}
function Ensure([bool]$test, [string]$name) {
    if (-not $test) { throw "FAIL: $name" }
    Write-Output "PASS: $name"
}
$originalOpacity = 100
$passed = $false
try {
    $startup = Call status
    $originalOpacity = [int]$startup.opacity
    $base = Call restore
    $before = Call list
    $pip = @($before.windows | Where-Object { $_.confirmedPiP } | Select-Object -First 1)
    if ($pip.Count -ne 1) { throw 'SKIP: no REAL verified PiP is open.' }
    $hwnd = [string]$pip[0].handle
    $original = [string]$pip[0].style
    Ensure ($pip[0].browser -eq 'Chrome' -or $pip[0].browser -eq 'Edge' -or
            $pip[0].browser -eq 'Brave') 'identified real browser window'
    Write-Output "TARGET: $($pip[0].browser) $hwnd; original style $original"

    $capture = Call capture $hwnd
    Ensure ($capture.capturedCount -eq 1) 'capture exact PiP HWND'
    $active = Call verify
    Ensure ($active.clickThrough -and $active.windows[0].transparent -and
            $active.windows[0].layered -and $active.windows[0].nonActivating) 'all 3 Win32 flags enabled'

    $interactive = Call off
    $off = Call verify
    Ensure (-not $off.clickThrough -and -not $off.windows[0].transparent) 'interactive restored'
    $on = Call on
    Ensure ($on.clickThrough) 're-enable click-through'

    $adjusted = Call opacity 75
    Ensure ($adjusted.opacity -eq 75) 'opacity set to 75'
    $resetAlpha = Call opacity ([string]$originalOpacity)
    Ensure ($resetAlpha.opacity -eq $originalOpacity) 'original opacity restored'
    $passed = $true
}
catch {
    Write-Output $_.Exception.Message
}
finally {
    if (-not $KeepCaptured) {
        $result = Call restore
        Ensure ($result.capturedCount -eq 0) 'all windows released'
    }
}
if ($passed) {
    Write-Output 'RESULT: PASS'
    exit 0
}
else {
    Write-Output 'RESULT: FAIL / SKIP'
    exit 1
}
