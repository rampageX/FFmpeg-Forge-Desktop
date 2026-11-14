# Headless-ish WinForms construction/interaction smoke on GitHub's Windows runner.
# This validates control creation, dark combos and repeated layout transitions;
# it is not a substitute for a user's visual flicker check on a GPU desktop.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$zip = Get-ChildItem dist -Filter 'FFMpegForgeLite.zip' | Select-Object -First 1
if (-not $zip) { throw 'Portable ZIP missing for UI smoke test' }
$root = Join-Path $env:RUNNER_TEMP 'forge-ui-smoke'
if (Test-Path $root) { Remove-Item $root -Recurse -Force }
Expand-Archive -LiteralPath $zip.FullName -DestinationPath $root
$exe = Join-Path $root 'FFmpegForge.exe'
$asm = [System.Reflection.Assembly]::LoadFrom($exe)
$mainType = $asm.GetType('FFmpegForge.MainForm', $true)
$comboType = $asm.GetType('FFmpegForge.DarkCombo', $true)
$flags = [System.Reflection.BindingFlags]'Instance,NonPublic,Public'
[System.Windows.Forms.Application]::EnableVisualStyles()
[System.Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)
# HDR preview must be mapped to SDR only in the JPEG comparison branches.
# These tests inspect the assembled FFmpeg commands; they cannot replace GPU/monitor checks.
$pvType = $asm.GetType('FFmpegForge.PreviewFrames', $true)
$jobType = $asm.GetType('FFmpegForge.Job', $true)
$settingsType = $asm.GetType('FFmpegForge.ConvSettings', $true)
$pvType.GetField('Dir').SetValue($null, $root)
$hdrJob = [Activator]::CreateInstance($jobType)
$hdrJob.ColorTransfer = 'smpte2084'
$hdrJob.ColorPrimaries = 'bt2020'
$hdrJob.ColorSpace = 'bt2020nc'
$hdrJob.ColorRange = 'tv'
$sdJob = [Activator]::CreateInstance($jobType)
$settings = [Activator]::CreateInstance($settingsType)
$checkCount = {
    param([string]$cmd, [string]$part, [int]$want)
    $got = ([regex]::Matches($cmd, [regex]::Escape($part))).Count
    if ($got -ne $want) { throw "HDR preview expected $want matches of [$part], got $got" }
}
$raw = '-hide_banner -i "input.mkv" -vf "eq=contrast=1.1" -c:v libx265 "output.mkv"'
$hdrCmd = $pvType.GetMethod('ForCommand').Invoke($null, @($raw, $settings, [double]9, $hdrJob))
& $checkCount $hdrCmd 'tonemap=tonemap=hable:desat=2' 2
& $checkCount $hdrCmd 'tin=smpte2084' 2
& $checkCount $hdrCmd 'format=rgb24' 2
$plain = $pvType.GetMethod('ForCommand').Invoke($null, @($raw, $settings, [double]9, $sdJob))
& $checkCount $plain 'tonemap=tonemap=hable' 0
$withSdr = $pvType.GetMethod('ForCommand').Invoke($null,
    @('-i "input.mkv" -vf "zscale=t=linear,tonemap=tonemap=hable,zscale=t=bt709" out.mkv', $settings, [double]9, $hdrJob))
& $checkCount $withSdr 'tonemap=tonemap=hable:desat=2' 1
$hlg = [Activator]::CreateInstance($jobType)
$hlg.ColorTransfer = 'arib-std-b67'
$hlg.ColorPrimaries = 'bt2020'
$hlg.ColorSpace = 'bt2020nc'
$hlg.ColorRange = 'tv'
$hlgCmd = $pvType.GetMethod('ForDecoder').Invoke($null, @('-i "hlg.mkv"', $settings, [double]9, $hlg))
& $checkCount $hlgCmd 'tin=arib-std-b67' 1
& $checkCount $hlgCmd 'format=rgb24' 1
$settings.SdrHdr = 1
$generatedHdr = $pvType.GetMethod('ForCommand').Invoke($null, @($raw, $settings, [double]9, $sdJob))
& $checkCount $generatedHdr 'tin=smpte2084' 1
$settings.SdrHdr = 0
$settings.RtxHdr = $true
$trueHdr = $pvType.GetMethod('ForEncoder').Invoke($null, @('-i "raw" out.mkv', $settings, $false, $false, [double]9, $sdJob))
& $checkCount $trueHdr 'tin=smpte2084' 1
Write-Host 'PASS: HDR10/HLG preview display mapping, SDR passthrough, no double tone-map, TrueHDR output'

Write-Host 'UI smoke: constructing main form.'
$form = [Activator]::CreateInstance($mainType)
try {
    Write-Host 'UI smoke: constructor finished. Creating an off-screen form handle.'
    [void]$form.Handle
    $form.PerformLayout()
    if (-not $form.IsHandleCreated) { throw 'Main form handle was not created' }
    $all = New-Object 'System.Collections.Generic.List[System.Windows.Forms.Control]'
    $walk = {
        param($root)
        foreach ($c in $root.Controls) {
            [void]$all.Add($c)
            & $walk $c
        }
    }
    & $walk $form
    # The scaling controls live in their own tool window; validate BOTH trees.
    $scaleField = $mainType.GetField('scaleWindow', $flags)
    if (-not $scaleField) { throw 'Missing independent Scale window' }
    $scaleForm = $scaleField.GetValue($form)
    if (-not $scaleForm -or $scaleForm.IsDisposed) { throw 'Scale window was not constructed' }
    [void]$scaleForm.Handle
    $mainCount = $all.Count
    & $walk $scaleForm
    $combos = @($all | Where-Object { $comboType.IsInstanceOfType($_) })
    $scroll = @($all | Where-Object { $_ -is [System.Windows.Forms.ScrollableControl] -and $_.AutoScroll })
    Write-Host "Main UI: $mainCount child controls. Scale tool: $($all.Count - $mainCount) child controls."
    Write-Host "Combined: $($combos.Count) dark combos, $($scroll.Count) scroll containers."
    if ($combos.Count -lt 42) { throw 'Scale and main dark combo controls were not preserved' }
    $numericType = $asm.GetType('FFmpegForge.DarkNumeric', $true)
    $numbers = @($all | Where-Object { $numericType.IsInstanceOfType($_) })
    Write-Host "Combined: $($numbers.Count) dark native numeric spinners."
    if ($numbers.Count -lt 18) { throw 'Numeric spinners disappeared during Scale move' }
    if ($scroll.Count -lt 2) { throw 'Main/Scale scroll containers missing' }
    foreach ($sectionName in @('secResVideo','secResImage','secScale','secOpti','secSeed','secVision','secRtx','secCai')) {
        $section = $mainType.GetField($sectionName, $flags).GetValue($form)
        if (-not $section) { throw "Missing scale section: $sectionName" }
        if (-not $section.Parent -or $section.Parent.FindForm() -ne $scaleForm) {
            throw "Scale control still in main settings: $sectionName"
        }
    }
    $wheelRouter = $mainType.GetField('wheelRouter', $flags).GetValue($form)
    if (-not $wheelRouter) { throw 'Settings mousewheel router missing' }
    Write-Host 'PASS: all Scale engines and output-resolution controls moved to the standalone window'
    $headerType = $asm.GetType('FFmpegForge.TopHeader', $true)
    $dropType = $asm.GetType('FFmpegForge.DropHint', $true)
    if ($headerType.GetField('art', $flags) -or $dropType.GetField('art', $flags)) {
        throw 'Decorative ArtCache is still attached to a header or empty list'
    }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Write-Host 'UI smoke: enumerating toggles.'
    $loadingField = $mainType.GetField('loadingSettings', $flags)
    if (-not $loadingField) { throw 'Missing initialization guard for AI install prompts' }
    $n = 0
    foreach ($fieldName in @('chkScale','chkOpti','chkDn','chkVsr','chkHdr','chkDlss','chkVision','chkSeed')) {
        $field = $mainType.GetField($fieldName, $flags)
        if (-not $field) { throw "Missing toggle: $fieldName" }
        $check = $field.GetValue($form)
        if ($null -eq $check) { throw "Null toggle: $fieldName" }
        $original = $check.Checked
        $one = [Diagnostics.Stopwatch]::StartNew()
        $priorLoading = $loadingField.GetValue($form)
        # AI Denoiser normally opens a modal install confirmation when first enabled.
        # In a headless CI test we must exercise layout WITHOUT waiting on that dialog.
        if ($fieldName -eq 'chkDn') { $loadingField.SetValue($form, $true) }
        try {
            Write-Host ("UI smoke: " + $fieldName + " first toggle")
            $check.Checked = -not $original
            Write-Host ("UI smoke: " + $fieldName + " first done in " + $one.ElapsedMilliseconds + " ms")
            $check.Checked = $original
            $one.Stop()
            Write-Host ("UI smoke: " + $fieldName + " restored in " + $one.ElapsedMilliseconds + " ms")
        } finally {
            $loadingField.SetValue($form, $priorLoading)
        }
        $n += 2
    }
    $sw.Stop()
    Write-Host 'UI smoke: toggle pass finished.'
    Write-Host "Dynamic layout toggles: $n transitions / $([Math]::Round($sw.Elapsed.TotalMilliseconds)) ms (unoptimized CI runner)."
    if ($form.IsDisposed) { throw 'Main window closed during layout changes' }
    Write-Host 'UI smoke: testing actual compatibility routes.'
    $getC = {
        param($name)
        $mainType.GetField($name, $flags).GetValue($form)
    }
    $scaleCheck = & $getC 'chkScale'
    $optiCheck = & $getC 'chkOpti'
    $seedCheck = & $getC 'chkSeed'
    $visionCheck = & $getC 'chkVision'
    $vsrCheck = & $getC 'chkVsr'
    $hdrCheck = & $getC 'chkHdr'
    $caiCheck = & $getC 'chkCai'
    foreach ($c in @($scaleCheck,$optiCheck,$seedCheck,$visionCheck,$vsrCheck,$hdrCheck,$caiCheck)) {
        $c.Checked = $false
    }
    $scaleCheck.Checked = $true
    $optiCheck.Checked = $true
    if (-not ($scaleCheck.Checked -and $optiCheck.Checked)) { throw 'Compatible Scale + OptiScaler pair was blocked' }
    $seedCheck.Checked = $true
    if ($scaleCheck.Checked -or $optiCheck.Checked) { throw 'SeedVR2 did not switch off incompatible Scale/Opti route' }
    $visionCheck.Checked = $true
    if ($seedCheck.Checked) { throw 'VisionNext did not switch off SeedVR2' }
    $vsrCheck.Checked = $true
    if ($visionCheck.Checked) { throw 'RTX did not switch off VisionNext' }
    $hdrCheck.Checked = $true
    if (-not $vsrCheck.Checked) { throw 'Compatible RTX VSR + TrueHDR pair was incorrectly blocked' }
    $caiCheck.Checked = $true
    if (-not $vsrCheck.Checked) { throw 'Custom AI preprocessing incorrectly blocked RTX route' }
    Write-Host 'PASS: route exclusivity preserves Scale+Opti, VSR+TrueHDR, and Custom AI preprocessing'

    # Show once to exercise actual child visibility, then hide/reopen.
    $scaleForm.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $scaleForm.Location = [System.Drawing.Point]::new(-1800, -1800)
    $scaleForm.Show()
    [System.Windows.Forms.Application]::DoEvents()
    $caiModel = $mainType.GetField('cbCaiModel', $flags).GetValue($form)
    if (-not $caiModel.Visible) { throw 'Custom AI options not expanded when Use my AI is ticked' }
    $caiCheck.Checked = $false
    if ($caiModel.Visible) { throw 'Custom AI options did not collapse when unchecked' }
    $scaleForm.Hide()
    $scaleForm.Show()
    [System.Windows.Forms.Application]::DoEvents()
    if (-not $scaleForm.Visible) { throw 'Scale tool window cannot be reopened' }
    # Category-aware migration: video/image resolution controls belong
    # solely to Scale, and still drive the same Snapshot() output settings.
    $mediaType = $asm.GetType('FFmpegForge.MediaKind', $true)
    $setCategory = $mainType.GetMethod('SetCategory', $flags)
    $snapshot = $mainType.GetMethod('Snapshot', $flags)
    $imageKind = [Enum]::Parse($mediaType, 'Image')
    $videoKind = [Enum]::Parse($mediaType, 'Video')
    $audioKind = [Enum]::Parse($mediaType, 'Audio')
    [void]$setCategory.Invoke($form, [object[]]@($imageKind))
    $imageSize = $mainType.GetField('secResImage', $flags).GetValue($form)
    $videoSize = $mainType.GetField('secResVideo', $flags).GetValue($form)
    if (-not $imageSize.Visible -or $videoSize.Visible) { throw 'Image Scale resolution card visibility broken' }
    if ($hdrCheck.Visible) { throw 'Video-only RTX TrueHDR shown in Image Scale mode' }
    if (-not $vsrCheck.Visible) { throw 'RTX VSR incorrectly hidden for Image output' }
    $snap = $snapshot.Invoke($form, $null)
    if (-not $snap.RtxVsr -or $snap.RtxHdr -or $snap.Dlss) { throw 'Image RTX settings no longer reach ConvSettings snapshot' }
    [void]$setCategory.Invoke($form, [object[]]@($audioKind))
    $openScale = $mainType.GetField('btnOpenScale', $flags).GetValue($form)
    if ($openScale.Enabled) { throw 'Scale launch not disabled in Audio mode' }
    [void]$setCategory.Invoke($form, [object[]]@($videoKind))
    if (-not $videoSize.Visible -or $imageSize.Visible -or -not $openScale.Enabled) {
        throw 'Video Scale controls did not return after changing category'
    }
    Write-Host 'PASS: Video/Image/Audio Scale card visibility and RTX image Snapshot'
    $scaleForm.Hide()
    Write-Host 'PASS: custom AI gated expansion and Scale window reopen'
    Write-Host 'PASS: main UI created and all toggle paths completed without exception'
}
finally {
    if (-not $form.IsDisposed) { $form.Close(); $form.Dispose() }
}
$mainSettings = $mainType.GetField('settings', $flags).GetValue($form)
if (-not $mainSettings.ContainsKey('ui_main_bounds') -or -not $mainSettings.ContainsKey('ui_scale_bounds')) {
    throw 'Closing main window did not save main + Scale window geometry'
}
Write-Host 'PASS: main shutdown persists both main and Scale window geometry'

# Verify the shared geometry helper independently: exact roundtrip, missing
# monitor / offscreen recovery, invalid settings safely ignored.
$placementType = $asm.GetType('FFmpegForge.WindowPlacement', $true)
$staticFlags = [Reflection.BindingFlags]'Static,NonPublic,Public'
$savePlacement = $placementType.GetMethod('Save', $staticFlags)
$restorePlacement = $placementType.GetMethod('Restore', $staticFlags)
$geomSettings = [System.Collections.Generic.Dictionary[string,string]]::new()
$working = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$testWidth = [Math]::Min(640, [Math]::Max(220, $working.Width - 80))
$testHeight = [Math]::Min(460, [Math]::Max(180, $working.Height - 80))
$testLeft = $working.Left + 25
$testTop = $working.Top + 25
$testForm = [System.Windows.Forms.Form]::new()
try {
    $testForm.MinimumSize = [System.Drawing.Size]::new(180, 150)
    $testForm.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $testForm.Bounds = [System.Drawing.Rectangle]::new($testLeft, $testTop, $testWidth, $testHeight)
    [void]$testForm.Handle
    [void]$savePlacement.Invoke($null, [object[]]@($testForm, $geomSettings, 'window_test'))
    if (-not $geomSettings.ContainsKey('window_test_bounds')) { throw 'Geometry not saved' }
    $testForm.Bounds = [System.Drawing.Rectangle]::new($working.Left + 4, $working.Top + 4, 220, 180)
    $ok = $restorePlacement.Invoke($null, [object[]]@($testForm, $geomSettings, 'window_test'))
    if (-not $ok -or $testForm.StartPosition -ne [System.Windows.Forms.FormStartPosition]::Manual) {
        throw 'Normal geometry could not be restored'
    }
    if ([Math]::Abs($testForm.Left - $testLeft) -gt 4 -or [Math]::Abs($testForm.Top - $testTop) -gt 4) {
        throw 'Saved window position did not roundtrip'
    }
    if ([Math]::Abs($testForm.Width - $testWidth) -gt 4 -or [Math]::Abs($testForm.Height - $testHeight) -gt 4) {
        throw 'Saved window size did not roundtrip'
    }
    # A second monitor was disconnected: coordinates must be moved to the
    # primary screen rather than leaving the window unreachable.
    $geomSettings['window_test_bounds'] = '100000,100000,640,480'
    $ok = $restorePlacement.Invoke($null, [object[]]@($testForm, $geomSettings, 'window_test'))
    $visible = [System.Drawing.Rectangle]::Intersect($testForm.Bounds, $working)
    if (-not $ok -or $visible.Width -lt 90 -or $visible.Height -lt 50) {
        throw 'Disconnected monitor left the restored window off-screen'
    }
    $geomSettings['window_test_bounds'] = 'this,is,not,a-window'
    if ($restorePlacement.Invoke($null, [object[]]@($testForm, $geomSettings, 'window_test'))) {
        throw 'Corrupted settings should not be restored'
    }
    Write-Host 'PASS: window position/size roundtrip, disconnected-monitor recovery and invalid settings guard'
}
finally {
    $testForm.Dispose()
}

# RIP layout regression. No disc is required: only WinForms controls and
# geometry are exercised. The old 6-column AutoSize table exceeded even 1525px.
Write-Host 'RIP smoke: constructing options window.'
$ripType = $asm.GetType('FFmpegForge.RipForm', $true)
$progressType = $asm.GetType('FFmpegForge.DarkProgressBar', $true)
$emptySettings = [System.Collections.Generic.Dictionary[string,string]]::new()
$ctor = @($ripType.GetConstructors() | Where-Object { $_.GetParameters().Count -eq 2 })[0]
if (-not $ctor) { throw 'RIP form constructor signature changed' }
Write-Host ("RIP constructor: " + $ctor.ToString())
$argsToRip = New-Object 'object[]' 2
$argsToRip[0] = $null
$argsToRip[1] = $emptySettings
# Reopening RIP restores its own window, not the main/Scale bounds.
$emptySettings['ui_rip_bounds'] = ($working.Left + 8).ToString() + ',' + ($working.Top + 8).ToString() + ',1020,760'
$emptySettings['ui_rip_max'] = '0'
$rip = $ctor.Invoke($argsToRip)
try {
    [void]$rip.Handle
    if ($rip.StartPosition -ne [System.Windows.Forms.FormStartPosition]::Manual) {
        throw 'RIP did not restore its saved geometry'
    }
    $root = $rip.Controls[0]
    # Hosted Windows CI uses a small virtual screen. Form.Size gets silently
    # clamped (~1028px), so size the internal layout directly to exercise
    # the actual 980 / 1180 / 1525px window widths.
    $root.Dock = [System.Windows.Forms.DockStyle]::None
    foreach ($screen in @(@(980, 720), @(1180, 860), @(1525, 953))) {
        $root.Size = New-Object System.Drawing.Size(($screen[0] - 16), ($screen[1] - 39))
        $root.PerformLayout()
        foreach ($c in $root.Controls) { $c.PerformLayout() }
        $quality = $ripType.GetField('cbQuality', $flags).GetValue($rip)
        $format  = $ripType.GetField('cbFormat', $flags).GetValue($rip)
        $subtitles = $ripType.GetField('cbSubs', $flags).GetValue($rip)
        $ripButton = $ripType.GetField('btnRip', $flags).GetValue($rip)
        $progress = $ripType.GetField('bar', $flags).GetValue($rip)
        $option = $quality.Parent
        $video = $ripType.GetField('cbVideo', $flags).GetValue($rip)
        Write-Host ("RIP synthetic viewport {0}x{1}: root={2}, opt={3}, Format.Width={4}, Video.Width={5}, Quality.Width={6}, RIP.Right={7}" -f
            $screen[0], $screen[1], $root.Bounds, $option.Bounds,
            $format.Width, $video.Width, $quality.Width, $ripButton.Parent.Right)
        foreach ($combo in @($format, $video, $quality, $subtitles)) {
            if ($combo.MaximumSize.Width -gt 0 -and $combo.Width -gt $combo.MaximumSize.Width + 2) {
                throw ("RIP field exceeds compact maximum width: " + $combo.Text + " / " + $combo.Width)
            }
        }
        if ($option.Right -gt $root.ClientSize.Width + 2) {
            throw "RIP options extend beyond root at $($screen[0])x$($screen[1])"
        }
        foreach ($combo in @($quality, $format, $subtitles)) {
            if ($combo.Right -gt $option.ClientSize.Width + 2) {
                throw "RIP combo beyond options viewport: $($combo.Name) at $($screen[0])x$($screen[1])"
            }
        }
        if (-not $progressType.IsInstanceOfType($progress)) {
            throw 'RIP native white ProgressBar has not been replaced'
        }
        if ($ripButton.Parent.Right -gt $root.ClientSize.Width + 2) {
            throw "RIP bottom action row clipped at $($screen[0])x$($screen[1])"
        }
    }
    Write-Host 'PASS: RIP options and bottom actions fit three window sizes; progress bar is dark'
}
finally {
    if (-not $rip.IsDisposed) { $rip.Close(); $rip.Dispose() }
}
if (-not $emptySettings.ContainsKey('ui_rip_bounds')) { throw 'RIP bounds not saved' }
Write-Host 'PASS: DVD RIP window remembers geometry in the same settings dictionary'
