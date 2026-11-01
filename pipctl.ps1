<#
.SYNOPSIS
  Local PowerShell CLI for the running PiP ClickThrough Windows app.
.EXAMPLE
  .\pipctl.ps1 list
  .\pipctl.ps1 capture auto
  .\pipctl.ps1 capture 0x125A0166
  .\pipctl.ps1 toggle
  .\pipctl.ps1 opacity 80
  .\pipctl.ps1 verify
  .\pipctl.ps1 restore
#>
param(
    [Parameter(Position=0)][string]$Command = 'status',
    [Parameter(Position=1)][string]$Arg = ''
)
$ErrorActionPreference = 'Stop'
if ($Command -eq 'help' -or $Command -eq '--help') {
    Write-Output 'Commands: status, list, capture [auto|0xHWND], on, off, toggle, opacity N, verify, refresh, restore, show'
    exit 0
}
$client = $null
$writer = $null
$reader = $null
try {
    $client = New-Object System.IO.Pipes.NamedPipeClientStream(
        '.', 'PiPClickThrough.Control.v1',
        [System.IO.Pipes.PipeDirection]::InOut,
        [System.IO.Pipes.PipeOptions]::None)
    $client.Connect(3500)
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    $writer = New-Object System.IO.StreamWriter($client, $utf8)
    $reader = New-Object System.IO.StreamReader($client, $utf8)
    $writer.AutoFlush = $true
    $writer.WriteLine((@{ command=$Command; arg=$Arg } | ConvertTo-Json -Compress))
    $result = $reader.ReadLine()
    if ([string]::IsNullOrWhiteSpace($result)) { throw 'No reply from application.' }
    $parsed = $result | ConvertFrom-Json
    $parsed | ConvertTo-Json -Depth 8
    if (-not $parsed.ok) { exit 2 }
    exit 0
}
catch {
    Write-Error ('PiP CLI failed: ' + $_.Exception.Message +
        '. Open PiPClickThrough.exe (normal mode) and retry.')
    exit 1
}
finally {
    # StreamReader and StreamWriter both own the same pipe by default.
    # Closing all three throws ObjectDisposedException in Windows PowerShell 5.1.
    if ($client -and $client.IsConnected) { $client.Dispose() }
}
