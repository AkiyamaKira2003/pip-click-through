<#
.SYNOPSIS
  Reproducible Windows x64 release build with the native Rust GUI.
.DESCRIPTION
  Requires stable Windows GNU Rust toolchain and LLVM (llvm-dlltool.exe).
  Never installs toolchains, modifies PATH permanently, or touches the legacy .NET release.
#>
param([switch]$SkipTests, [switch]$ArchiveOnly)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
$stage = $null
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
            & $cargo +stable-x86_64-pc-windows-gnu test --locked --all-targets
            if ($LASTEXITCODE -ne 0) { throw 'Rust tests failed.' }
        }
        & $cargo +stable-x86_64-pc-windows-gnu build --locked --release
        if ($LASTEXITCODE -ne 0) { throw 'Rust release build failed.' }
    }
    finally { $ErrorActionPreference = $oldErrorAction }
    if ($ArchiveOnly) {
        # Build a fresh archive without overwriting an EXE the user is running.
        $stage = Join-Path $bin ('archive-stage-' + [guid]::NewGuid().ToString('N'))
        if (Test-Path -LiteralPath $stage) { throw 'Archive stage already exists.' }
        New-Item -ItemType Directory -Path $stage | Out-Null
        $out = $stage
    }
    else {
        $out = Join-Path $PSScriptRoot 'Release-Portable-Rust'
        New-Item -ItemType Directory -Force -Path $out | Out-Null
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'target\release\PiPClickThrough.exe') -Destination $out -Force
    # The acceptance report contains local QA metadata and must remain in
    # source control only; never bundle it in a downloadable archive.
    foreach ($support in @('pipctl.ps1','pipctl.cmd','README.md')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $support) -Destination $out -Force
    }
    $dist = Join-Path $PSScriptRoot 'dist'
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Cargo.toml') -Raw
    $version = ([regex]::Match($manifest, '(?m)^version\s*=\s*"([^"]+)"')).Groups[1].Value
    if (-not $version) { throw 'Could not read Cargo package version' }
    $zip = Join-Path $dist "PiPClickThrough-Rust-$version-win-x64.zip"
    # Enumerate only distributable files: old local output directories may
    # contain stale acceptance reports or other private diagnostic artifacts.
    $archiveMembers = @('PiPClickThrough.exe','README.md','pipctl.ps1','pipctl.cmd') |
        ForEach-Object { Join-Path $out $_ }
    Compress-Archive -LiteralPath $archiveMembers -DestinationPath $zip -CompressionLevel Optimal -Force
    if ($ArchiveOnly) {
        Write-Output 'Archive-only mode: existing portable installation was not replaced.'
    }
    else {
        Write-Output ('Built native Rust release: '+(Join-Path $out 'PiPClickThrough.exe'))
    }
    Write-Output ('Packaged portable archive: '+$zip)
}
finally {
    Pop-Location
    if ($stage -and (Test-Path -LiteralPath $stage)) {
        $binRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '.build-tools')).TrimEnd('\') + '\'
        $fullStage = [IO.Path]::GetFullPath($stage)
        if (-not $fullStage.StartsWith($binRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Unsafe archive staging path; refusing cleanup.'
        }
        Remove-Item -LiteralPath $fullStage -Recurse -Force
    }
}
