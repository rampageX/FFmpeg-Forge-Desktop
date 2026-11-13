# Headless-ish WinForms construction/interaction smoke on GitHub's Windows runner.
# This validates control creation, dark combos and repeated layout transitions;
# it is not a substitute for a user's visual flicker check on a GPU desktop.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$zip = Get-ChildItem dist -Filter '*-Portable.zip' | Select-Object -First 1
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
    $combos = @($all | Where-Object { $comboType.IsInstanceOfType($_) })
    $scroll = @($all | Where-Object { $_ -is [System.Windows.Forms.ScrollableControl] -and $_.AutoScroll })
    Write-Host "Main UI: $($all.Count) child controls, $($combos.Count) dark combos, $($scroll.Count) auto-scroll containers."
    if ($combos.Count -lt 25) { throw 'Missing dark combo controls from the main form' }
    if ($scroll.Count -lt 1) { throw 'Settings scroll container not found' }
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
    Write-Host 'PASS: main UI created and all toggle paths completed without exception'
}
finally {
    if (-not $form.IsDisposed) { $form.Close(); $form.Dispose() }
}
