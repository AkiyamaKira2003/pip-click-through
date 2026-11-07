<#
.SYNOPSIS
  Reproducible Windows x64 release build with the native Rust GUI.
.DESCRIPTION
  Requires stable Windows GNU Rust toolchain and LLVM (llvm-dlltool.exe).
  Never installs toolchains, modifies PATH permanently, or touches the legacy .NET release.
#>
param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $cargo = Join-Path $env:USERPROFILE '.cargo\bin\cargo.exe'
    if (-not (Test-Path -LiteralPath $cargo)) { throw 'Install Rust via https://rustup.rs first.' }
    $llvm = @('C:\Program Files\LLVM\bin\llvm-dlltool.exe') +
        @((Get-Command llvm-dlltool.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source))
    $dlltool = $llvm | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
    if (-not $dlltool) { throw 'LLVM llvm-dlltool.exe not found. Install LLVM.LLVM via winget.' }
    $bin = Join-Path $PSScriptRoot '.build-tools'
    New-Item -ItemType Directory -Path $bin -Force | Out-Null
    Copy-Item -LiteralPath $dlltool -Destination (Join-Path $bin 'dlltool.exe') -Force
    $env:PATH = "$bin;$($env:USERPROFILE)\.cargo\bin;$env:PATH"
    $env:RUSTFLAGS = '-C linker=rust-lld'
    # Cargo writes progress to stderr even on success; under Windows PowerShell
    # 5.1 that becomes a NativeCommandError when ErrorActionPreference=Stop.
    # Check the actual process exit code instead of treating progress as failure.
    $oldErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        if (-not $SkipTests) {
            & $cargo +stable-x86_64-pc-windows-gnu test --all-targets
            if ($LASTEXITCODE -ne 0) { throw 'Rust tests failed.' }
        }
        & $cargo +stable-x86_64-pc-windows-gnu build --release
        if ($LASTEXITCODE -ne 0) { throw 'Rust release build failed.' }
    }
    finally { $ErrorActionPreference = $oldErrorAction }
    $out = Join-Path $PSScriptRoot 'Release-Portable-Rust'
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'target\release\PiPClickThrough.exe') -Destination $out -Force
    foreach ($support in @('pipctl.ps1','pipctl.cmd','README.md','RUST-ACCEPTANCE.md')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $support) -Destination $out -Force
    }
    $dist = Join-Path $PSScriptRoot 'dist'
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    $zip = Join-Path $dist 'PiPClickThrough-Rust-2.0.0-win-x64.zip'
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -CompressionLevel Optimal -Force
    Write-Output ('Built native Rust release: '+(Join-Path $out 'PiPClickThrough.exe'))
    Write-Output ('Packaged portable archive: '+$zip)
}
finally { Pop-Location }
