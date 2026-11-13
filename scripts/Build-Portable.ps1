# Build a slim Windows portable package from verified v2.3.0 release source,
# maintained in LiteSource/. Upstream GitHub main still carries old v1.1.3a source;
# never build from it. The official release archive supplies only auxiliary binaries.
[CmdletBinding()]
param(
    [string]$UpstreamTag = '2.3.0',
    [string]$Suffix = 'rampage.1',
    [string]$OutputDir = 'dist'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Set-StrictMode -Version Latest

if ($UpstreamTag -notmatch '^\d+\.\d+\.\d+$') { throw "Unexpected upstream tag: $UpstreamTag" }
if ($Suffix -notmatch '^rampage\.\d+$') { throw "Unexpected suffix: $Suffix" }

foreach ($tool in @('gh', '7z', 'cmd.exe')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool is required" }
}
if (-not $env:GH_TOKEN) { throw 'GH_TOKEN must be set for downloading the upstream release' }

$root = Join-Path ([System.IO.Path]::GetTempPath()) ("forge-build-" + [Guid]::NewGuid().ToString('N'))
$original = Join-Path $root 'original'
$unpack = Join-Path $root 'unpack'
$working = Join-Path $root 'working'
$stage = Join-Path $root 'stage'
$destination = [System.IO.Path]::GetFullPath($OutputDir)
foreach ($d in @($original, $unpack, $working, $stage, $destination)) {
    New-Item -ItemType Directory -Path $d -Force | Out-Null
}
try {
    $archive = "FFmpegForge-$UpstreamTag-Portable.rar"
    Write-Host "Downloading official archive: $UpstreamTag / $archive"
    & gh release download $UpstreamTag -R Aaronstanley78/FFmpeg-Forge-Desktop -p $archive -D $original
    if ($LASTEXITCODE -ne 0) { throw 'Upstream archive download failed' }
    & 7z x -y "-o$unpack" (Join-Path $original $archive) | Select-Object -Last 4
    if ($LASTEXITCODE -ne 0) { throw '7z archive extraction failed' }

    $app = Join-Path $unpack 'FFmpeg'
    $sourceDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../LiteSource'))
    if (-not (Test-Path (Join-Path $sourceDir 'src/Controls.cs'))) { throw 'Release Source/src/Controls.cs not found' }
    $program = Get-Content (Join-Path $sourceDir 'src/Program.cs') -Raw
    $versionMatch = [regex]::Match($program, 'AssemblyInformationalVersion\("([^"]+)"\)')
    if (-not $versionMatch.Success -or $versionMatch.Groups[1].Value -ne $UpstreamTag) {
        throw "Release source version mismatch: expected $UpstreamTag; got '$($versionMatch.Groups[1].Value)'"
    }

    # Keep the tracked LiteSource source intact: build in an isolated working copy.
    Copy-Item -LiteralPath $sourceDir -Destination (Join-Path $working 'Source') -Recurse
    $buildSource = Join-Path $working 'Source'
    $controls = Join-Path $buildSource 'src/Controls.cs'
    $code = [System.IO.File]::ReadAllText($controls)
    # The two images are disabled directly in maintained source, rather than
    # applying a fragile regex to newer upstream sources during each build.
    if ($code -match 'art\.For\(ClientSize\)' -or $code -notmatch 'Flat dark header') {
        throw "LiteSource banner/drop patch is missing; refusing to package decorative artwork."
    }
    if (-not (Test-Path (Join-Path $buildSource 'src/DarkControls.cs'))) {
        throw 'LiteSource native dark UI controls are missing'
    }
    Write-Host 'UI source verified: both artworks disabled; dark native controls included.'

    # Reuse upstream's build command (resources and references are version-specific);
    # disable its interactive pause and validate the newly built binary.
    $bat = Get-Content (Join-Path $buildSource 'build.bat') -Raw
    # The unused decorative images should not be embedded in the EXE.
    foreach ($resource in @('/resource:art\header.jpg,header.jpg', '/resource:art\drop.jpg,drop.jpg')) {
        if (-not $bat.Contains($resource)) { throw "Expected resource absent: $resource" }
        $bat = $bat.Replace($resource, '')
    }
    $bat = [regex]::Replace($bat, '(?im)^[ \t]*pause[ \t]*$', 'rem CI: no interactive pause')
    [System.IO.File]::WriteAllText((Join-Path $buildSource 'build-ci.bat'), $bat, [System.Text.Encoding]::Default)
    Push-Location $buildSource
    try {
        & cmd.exe /d /c build-ci.bat
        if ($LASTEXITCODE -ne 0) { throw "C# compiler failed with exit code $LASTEXITCODE" }
    }
    finally { Pop-Location }
    $exe = Join-Path $working 'FFmpegForge.exe'
    if (-not (Test-Path $exe)) { throw 'C# build did not produce a new FFmpegForge.exe' }
    $assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($exe).Version.ToString()
    if ($assemblyVersion -ne "$UpstreamTag.0") { throw "Compiled EXE version mismatch: $assemblyVersion" }
    Write-Host "Newly compiled FFmpegForge.exe: $((Get-Item $exe).Length) bytes; version $assemblyVersion"

    Copy-Item $exe $stage
    foreach ($name in @('ForgeRTX.exe', 'dovi_tool.exe', 'LICENSE-dovi_tool.txt', 'LICENSE-GPL-3.0.txt', 'SHADER_LICENSES.txt', 'portable.txt')) {
        $candidate = Join-Path $app $name
        if (-not (Test-Path $candidate)) { throw "Required runtime file missing from upstream release: $name" }
        Copy-Item -LiteralPath $candidate -Destination $stage
    }
    if (Test-Path (Join-Path $app 'models')) {
        Copy-Item -LiteralPath (Join-Path $app 'models') -Destination $stage -Recurse
    }
    # Do not copy bin/, FFmpeg/, Source/ or any bundled ffmpeg/ffprobe/ffplay executables.
    $readme = @'
FFmpeg Forge — Rampage Lite
==========================
No FFmpeg binaries are bundled with this portable release.
Start FFmpegForge.exe and choose "FFmpeg location..." to point to your own
ffmpeg.exe. Keep ffprobe.exe beside that ffmpeg.exe for media metadata.
The saved FFmpeg path is reused; PATH and conventional locations are also scanned.
The portable.txt file keeps settings in the application's directory.

This version is built from the tracked LiteSource/ (official 2.3.0 Source).
Both decorative UI background images are disabled. Scrollbars and dropdown
arrows use the dark Windows theme, with batched/anti-flicker settings layouts.
Other processing features remain based on the upstream 2.3.0 version.

ForgeRTX.exe supports NVIDIA RTX driver processing; dovi_tool.exe is included
for Dolby Vision workflows. Their original license notices are included.
The FFmpeg/ documentation directory and all three FFmpeg executables are
deliberately excluded.
'@
    [System.IO.File]::WriteAllText((Join-Path $stage 'README-LITE.txt'), $readme, [System.Text.UTF8Encoding]::new($false))
    $outputName = "FFmpegForge-$UpstreamTag-$Suffix-Portable.zip"
    $zip = Join-Path $destination $outputName
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $z = [System.IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $entries = @($z.Entries | ForEach-Object FullName)
        if ($entries -notcontains 'FFmpegForge.exe') { throw 'Portable ZIP has no application EXE' }
        foreach ($entry in $entries) {
            if ($entry -match '(?i)^(bin|FFmpeg|Source)/' -or
                $entry -match '(?i)(^|/)(ffmpeg|ffprobe|ffplay)\.exe$') {
                throw "Forbidden bundled FFmpeg content: $entry"
            }
        }
        Write-Host ("ZIP manifest (" + $entries.Count + " entries):")
        $entries | ForEach-Object { Write-Host "  $_" }
    }
    finally { $z.Dispose() }
    $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash
    Write-Host "ZIP: $zip"
    Write-Host "SHA256: $hash"
    if ($env:GITHUB_OUTPUT) {
        "zip=$zip" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "name=$outputName" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "sha256=$hash" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "version=$UpstreamTag" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    }
}
finally {
    if (Test-Path $root) { Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue }
}
