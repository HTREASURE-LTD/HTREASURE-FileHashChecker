<#
.SYNOPSIS
    Builds, tests, signs and packages HT File Hash Checker.

.DESCRIPTION
    Produces in release\v<version>\:
      HTFileHashChecker_Setup_v<version>.exe          Inno Setup installer (selects the .NET 4.8 or 4.6.1 build)
      HTFileHashChecker_Portable_v<version>.zip       portable edition (no installation, no registry changes)
      HTFileHashChecker_Source_v<version>.zip         source of the committed HEAD, including the submodules
      HTFileHashChecker_Documentation_v<version>.zip  user and technical documentation
      SHA256SUMS.txt                                  SHA-256 of the files above (GNU format), computed last

    HTREASURE standards: Release builds must sign the bundled advisory snapshot with the offline advisory key
    (-AdvisorySigningKey / -AdvisoryKeyId). -InternalAdvisorySnapshot builds with an unsigned snapshot instead; the
    application then shows "Development build: the bundled advisory snapshot is not signed". Do not publish such a
    build as a stable release.

    Code signing uses scripts\sign-file.ps1 (certificate outside the repository, default C:\HTSoftware\signing).

.EXAMPLE
    powershell -NoProfile -File scripts\build-release.ps1 -AdvisorySigningKey D:\offline\advisory.pem -AdvisoryKeyId htreasure-advisory-2026
#>
param(
    [string]$AdvisorySigningKey,
    [string]$AdvisoryKeyId = 'htreasure-advisory-2026',
    [switch]$InternalAdvisorySnapshot,
    [switch]$SkipCodeSigning,
    [switch]$SkipTests,
    [string]$SigningDir = 'C:\HTSoftware\signing',
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

function Step($text) { Write-Host ''; Write-Host "== $text" -ForegroundColor Cyan }
function Run($exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe failed with exit code $LASTEXITCODE" }
}
function New-Zip($sourceDir, $zipPath) {
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    # Entries are written one by one with '/' separators (ZIP specification). ZipFile.CreateFromDirectory would write
    # '\' under Windows PowerShell 5.1, which other tools extract as file names containing backslashes.
    $base = (Get-Item $sourceDir).Parent.FullName.TrimEnd('\') + '\'
    $stream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
    $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($f in (Get-ChildItem -Recurse -File $sourceDir | Sort-Object FullName)) {
            $name = $f.FullName.Substring($base.Length).Replace('\', '/')
            $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $f.LastWriteTime
            $es = $entry.Open(); $fs = [IO.File]::OpenRead($f.FullName)
            try { $fs.CopyTo($es) } finally { $fs.Dispose(); $es.Dispose() }
        }
    } finally { $zip.Dispose(); $stream.Dispose() }
}

Set-Location $Root
$version = ([xml](Get-Content (Join-Path $Root 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$out = Join-Path $Root "release\v$version"
$stage = Join-Path $Root 'release\stage'
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) not found.' }
$signScript = Join-Path $PSScriptRoot 'sign-file.ps1'

Step "HT File Hash Checker $version"
if (-not (Test-Path (Join-Path $Root 'external\ht-software-standards\dotnet')) -or -not (Test-Path (Join-Path $Root 'external\HT-Software-Template\HTREASURE-Branding'))) {
    throw 'Submodules missing. Run: git submodule update --init'
}
$dirty = git status --porcelain
if ($dirty) { Write-Warning 'The working tree has uncommitted changes. The source package contains the committed HEAD only.' }
$commit = (git rev-parse HEAD).Trim()

# ------------------------------------------------------------------ build
$buildArgs = @('build', 'HTFileHashChecker.sln', '-c', 'Release', '-nologo', '-v', 'minimal', '-p:HTCopyrightYear=2026')
if ($AdvisorySigningKey) {
    if (-not (Test-Path $AdvisorySigningKey)) { throw "Advisory signing key not found: $AdvisorySigningKey" }
    $buildArgs += "-p:HTAdvisorySigningKey=$AdvisorySigningKey", "-p:HTAdvisoryKeyId=$AdvisoryKeyId"
} elseif ($InternalAdvisorySnapshot) {
    Write-Warning 'Internal build: the bundled advisory snapshot is NOT signed. Do not publish this build as a stable release.'
    $buildArgs += '-p:HTRequireSignedSnapshot=false'
} else {
    throw 'Pass -AdvisorySigningKey <pem> (release) or -InternalAdvisorySnapshot (internal test build).'
}
Step 'Building Release (net48 and net461)'
Run 'dotnet' $buildArgs

if (-not $SkipTests) {
    Step 'Running automated tests (net48 and net462)'
    Run 'dotnet' @('test', 'HTFileHashChecker.sln', '-c', 'Release', '--no-build', '-nologo', '--logger', 'trx;LogFileName=release-tests.trx', '--results-directory', (Join-Path $Root 'TestResults'))
}

# ------------------------------------------------------------------ stage
Step 'Staging files'
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
$docsStage = Join-Path $stage 'docs'
New-Item -ItemType Directory -Force $docsStage | Out-Null
foreach ($tf in 'net48', 'net461') {
    $src = Join-Path $Root "src\HTFileHashChecker.UI\bin\Release\$tf"
    $dst = Join-Path $stage "app\$tf"
    New-Item -ItemType Directory -Force $dst | Out-Null
    Copy-Item -Recurse -Path (Join-Path $src '*') -Destination $dst -Exclude '*.pdb'
    if (Test-Path (Join-Path $dst 'HTFileHashChecker.portable')) { throw 'A portable marker must not be in the build output.' }
}
Copy-Item (Join-Path $Root 'LICENSE') (Join-Path $docsStage 'LICENSE.txt')
Copy-Item (Join-Path $Root 'installer\PRIVACY.txt') (Join-Path $docsStage 'PRIVACY.txt')
$ofl = Get-Content -Raw (Join-Path $Root 'external\HT-Software-Template\HTREASURE-Branding\Source\Fonts\Inter\OFL-LICENSE.txt')
(Get-Content -Raw (Join-Path $Root 'THIRD_PARTY_NOTICES.md')) + "`r`n`r`n---- Inter: SIL Open Font License 1.1 ----`r`n`r`n" + $ofl |
    Set-Content -Encoding UTF8 (Join-Path $docsStage 'THIRD_PARTY_NOTICES.txt')
Copy-Item (Join-Path $Root 'installer\README.txt') (Join-Path $docsStage 'README.txt')

# ------------------------------------------------------------------ code signing
$signed = $false
if (-not $SkipCodeSigning) {
    Step 'Signing application files (self-signed HTREASURE LTD certificate)'
    $files = Get-ChildItem -Recurse (Join-Path $stage 'app') -Include 'HTFileHashChecker*.exe', 'HTFileHashChecker*.dll', 'HTREASURE*.dll'
    foreach ($f in $files) { Run 'powershell.exe' @('-NoProfile', '-File', $signScript, $f.FullName, '-SigningDir', $SigningDir) }
    $signed = $true
}

# ------------------------------------------------------------------ installer
Step 'Building the installer'
New-Item -ItemType Directory -Force $out | Out-Null
Get-ChildItem $out -File | Remove-Item -Force
$isccArgs = @("/DStage=$stage", "/DOutputRoot=$out", '/Q')
if ($signed) {
    $isccArgs += "/Shtsign=powershell.exe -NoProfile -File `$q$signScript`$q -SigningDir `$q$SigningDir`$q `$f", '/DSignTool=htsign'
}
$isccArgs += (Join-Path $Root 'installer\HTFileHashChecker.iss')
Run $iscc $isccArgs
$setup = Join-Path $out "HTFileHashChecker_Setup_v$version.exe"

# ------------------------------------------------------------------ portable
Step 'Building the portable edition'
$pname = "HTFileHashChecker_Portable_v$version"
$pdir = Join-Path $stage "portable\$pname"
New-Item -ItemType Directory -Force $pdir | Out-Null
Copy-Item -Recurse (Join-Path $stage 'app\net48\*') $pdir
$win8 = Join-Path $pdir 'Windows 8.0 (NET 4.6.1)'
New-Item -ItemType Directory -Force $win8 | Out-Null
Copy-Item -Recurse (Join-Path $stage 'app\net461\*') $win8
foreach ($d in $pdir, $win8) {
    Set-Content -Encoding ASCII (Join-Path $d 'HTFileHashChecker.portable') 'Portable mode: settings and logs are kept in the Data folder next to the program.'
}
Copy-Item (Join-Path $docsStage '*') $pdir
if (Test-Path (Join-Path $Root 'signing\HTREASURE-CodeSigning.cer')) { Copy-Item (Join-Path $Root 'signing\HTREASURE-CodeSigning.cer') $pdir }
New-Zip $pdir (Join-Path $out "$pname.zip")

# ------------------------------------------------------------------ source
Step "Packaging source ($commit)"
$sname = "HTFileHashChecker_Source_v$version"
$sdir = Join-Path $stage "source\$sname"
New-Item -ItemType Directory -Force $sdir | Out-Null
$tar = Join-Path $stage 'source.tar'
Run 'git' @('archive', '--format=tar', "--output=$tar", 'HEAD')
Run 'tar' @('-xf', $tar, '-C', $sdir)
foreach ($sub in (git submodule status | ForEach-Object { ($_.Trim() -split '\s+')[1] })) {
    $subTar = Join-Path $stage 'sub.tar'
    Run 'git' @('-C', $sub, 'archive', '--format=tar', "--output=$subTar", 'HEAD')
    New-Item -ItemType Directory -Force (Join-Path $sdir $sub) | Out-Null
    Run 'tar' @('-xf', $subTar, '-C', (Join-Path $sdir $sub))
}
Set-Content -Encoding ASCII (Join-Path $sdir 'SOURCE-COMMIT.txt') "HT File Hash Checker $version source, commit $commit (submodules included)."
New-Zip $sdir (Join-Path $out "$sname.zip")

# ------------------------------------------------------------------ documentation
Step 'Packaging documentation'
$dname = "HTFileHashChecker_Documentation_v$version"
$ddir = Join-Path $stage "documentation\$dname"
New-Item -ItemType Directory -Force $ddir | Out-Null
Get-ChildItem $Root -File -Filter '*.md' | Copy-Item -Destination $ddir
if (Test-Path (Join-Path $Root 'docs')) { Copy-Item -Recurse (Join-Path $Root 'docs\*') $ddir }
Copy-Item (Join-Path $docsStage '*') $ddir
New-Zip $ddir (Join-Path $out "$dname.zip")

# ------------------------------------------------------------------ checksums (last, from the finished files)
Step 'Writing SHA256SUMS.txt'
$artifacts = Get-ChildItem $out -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object Name
$lines = foreach ($a in $artifacts) { (Get-FileHash -Algorithm SHA256 $a.FullName).Hash.ToLowerInvariant() + '  ' + $a.Name }
[IO.File]::WriteAllText((Join-Path $out 'SHA256SUMS.txt'), (($lines -join "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))

Step 'Done'
Get-ChildItem $out -File | Format-Table Name, Length -AutoSize | Out-String | Write-Host
Write-Host "Code signing: $(if ($signed) { 'self-signed HTREASURE LTD certificate (not trusted by Windows unless the certificate is installed)' } else { 'none' })"
Write-Host "Advisory snapshot: $(if ($AdvisorySigningKey) { 'signed' } else { 'UNSIGNED (internal build)' })"
