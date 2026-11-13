# FFmpeg Forge Lite 2.3.2: canonical source is in src/, build output in dist/.
# Upstream 2.3.0 RAR is a verified immutable binary-helper baseline, not source.
[CmdletBinding()]
param(
    [string]$Version = '2.3.2',
    [string]$UpstreamTag = '2.3.0',
    [string]$OutputDir = 'dist'
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Set-StrictMode -Version Latest

if ($Version -ne '2.3.2' -or $UpstreamTag -ne '2.3.0') {
    throw 'Unsupported version/base combination. Review source and helper baseline before upgrading.'
}
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dest = [IO.Path]::GetFullPath((Join-Path $repo $OutputDir))
New-Item -Type Directory -Path $dest -Force | Out-Null

$programPath = Join-Path $repo 'src/Program.cs'
if (-not (Test-Path $programPath)) { throw 'Missing canonical src/Program.cs' }
$program = [IO.File]::ReadAllText($programPath)
if ($program -notmatch 'AssemblyInformationalVersion\("2\.3\.2"\)' -or
    $program -notmatch 'AssemblyVersion\("2\.3\.2\.0"\)') {
    throw 'Canonical source is not 2.3.2. Refusing to package.'
}
$ui = [IO.File]::ReadAllText((Join-Path $repo 'src/Controls.cs'))
if ($ui -match 'art\.For\(ClientSize\)' -or $ui -notmatch 'Flat dark header' -or
    -not (Test-Path (Join-Path $repo 'src/DarkNumeric.cs'))) {
    throw 'Approved 2.3.2 dark UI fixes missing; refusing to package.'
}
foreach ($tool in @('cmd.exe', 'gh', '7z')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "Required tool missing: $tool" }
}
if (-not $env:GH_TOKEN) { throw 'GH_TOKEN is required for the upstream baseline download' }

Push-Location $repo
try {
    & cmd.exe /d /c build.bat
    if ($LASTEXITCODE -ne 0) { throw "C# compiler returned $LASTEXITCODE" }
}
finally { Pop-Location }
$exe = Join-Path $dest 'FFmpegForge.exe'
if (-not (Test-Path $exe)) { throw 'build.bat did not generate dist/FFmpegForge.exe' }
$exeVersion = [Reflection.AssemblyName]::GetAssemblyName($exe).Version.ToString()
if ($exeVersion -ne '2.3.2.0') { throw "Compiled assembly version mismatch: $exeVersion" }
Write-Host "PASS: locally compiled EXE version = $exeVersion, bytes = $((Get-Item $exe).Length)"

$tmp = Join-Path ([IO.Path]::GetTempPath()) ('forge232-' + [Guid]::NewGuid().ToString('N'))
$download = Join-Path $tmp 'upstream'
$unpack = Join-Path $tmp 'unpack'
$stage = Join-Path $tmp 'stage'
foreach ($d in @($download,$unpack,$stage)) { New-Item -Type Directory -Path $d -Force | Out-Null }
try {
    # Use the ORIGINAL upstream RAR, not a C# source checkout. GitHub cannot
    # accept the 209 MB binary in normal Git, so an exact SHA256-verified copy
    # is retained in this fork as the archive-upstream-2.3.0 draft Release.
    $archive = Join-Path $download 'FFmpegForge-2.3.0-Portable.rar'
    $local = Join-Path $repo 'archive/upstream/v2.3.0/FFmpegForge-2.3.0-Portable.rar'
    if ((Test-Path $local) -and (Get-Item $local).Length -eq 209148735) {
        Copy-Item $local $archive
    }
    else {
        & gh release download $UpstreamTag -R Aaronstanley78/FFmpeg-Forge-Desktop -p 'FFmpegForge-2.3.0-Portable.rar' -D $download
        if ($LASTEXITCODE -ne 0) { throw 'Failed to download original upstream RAR' }
    }
    $expected = 'C63F278FFAFADBA93374AC0C5153B802B4C5E086B0515AF7AD2D95DA9B25DCC9'
    if ((Get-Item $archive).Length -ne 209148735 -or
        (Get-FileHash $archive -Algorithm SHA256).Hash -ne $expected) {
        throw 'Upstream RAR baseline is corrupt or differs from the verified original'
    }
    Write-Host 'PASS: pristine upstream 2.3.0 RAR SHA256/size verified.'
    & 7z x -y "-o$unpack" $archive | Select-Object -Last 4
    if ($LASTEXITCODE -ne 0) { throw 'Cannot extract upstream helper files' }
    $runtime = Join-Path $unpack 'FFmpeg'
    Copy-Item -LiteralPath $exe -Destination $stage
    $helperFiles = @('ForgeRTX.exe', 'dovi_tool.exe', 'LICENSE-dovi_tool.txt',
                     'LICENSE-GPL-3.0.txt', 'SHADER_LICENSES.txt', 'portable.txt')
    foreach ($name in $helperFiles) {
        $source = Join-Path $runtime $name
        if (-not (Test-Path $source)) { throw "Missing upstream runtime helper: $name" }
        Copy-Item -LiteralPath $source -Destination $stage
    }
    if (Test-Path (Join-Path $runtime 'models')) {
        Copy-Item -LiteralPath (Join-Path $runtime 'models') -Destination $stage -Recurse
    }
    $readme = @'
FFmpeg Forge Lite 2.3.2 (Windows portable)
==========================================
Extract the ZIP and run FFmpegForge.exe.
Click "FFmpeg location..." and choose your own ffmpeg.exe.
Keep ffprobe.exe alongside ffmpeg.exe for media metadata and progress.
The package does NOT bundle FFmpeg, FFprobe or FFplay executables.

Based on the official 2.3.0 release source; Lite's WinForms UI fixes
remove decorative backgrounds, darken scrollbars/combos/spinner arrows,
reduce expansion flicker and correct the DVD/Blu-ray rip window layout.

ForgeRTX.exe and dovi_tool.exe are upstream auxiliary tools.
They retain their upstream license notices in this package.
portable.txt keeps application settings beside the application.
'@
    [IO.File]::WriteAllText((Join-Path $stage 'README-LITE.txt'),
        $readme, [Text.UTF8Encoding]::new($false))
    $zip = Join-Path $dest 'FFMpegForgeLite.zip'
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $z = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $entries = @($z.Entries | ForEach-Object FullName)
        if ($entries -notcontains 'FFmpegForge.exe') { throw 'ZIP is missing the new EXE' }
        foreach ($entry in $entries) {
            if ($entry -match '(?i)(^|/)(ffmpeg|ffprobe|ffplay)\.exe$' -or
                $entry -match '(?i)^(bin|FFmpeg|Source|LiteSource)/') {
                throw "Forbidden bundled-FFmpeg/old-source content in ZIP: $entry"
            }
        }
        Write-Host ("PASS: ZIP manifest checked (" + $entries.Count + " entries):")
        $entries | ForEach-Object { Write-Host "  $_" }
    }
    finally { $z.Dispose() }
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash
    Write-Host "PASS: $zip"
    Write-Host "SHA256: $hash"
    if ($env:GITHUB_OUTPUT) {
        "name=FFMpegForgeLite.zip" | Out-File $env:GITHUB_OUTPUT -Append -Encoding utf8
        "version=$Version" | Out-File $env:GITHUB_OUTPUT -Append -Encoding utf8
        "sha256=$hash" | Out-File $env:GITHUB_OUTPUT -Append -Encoding utf8
    }
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
