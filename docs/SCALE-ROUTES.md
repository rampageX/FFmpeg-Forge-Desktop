# FFmpeg Forge Lite — Scale window / execution-route compatibility

This document records **actual v2.3.1 source behavior**, not a claim that every
GPU combination was tested on real hardware. Engine checks are in
`src/Scaling.cs`, `src/Rtx.cs`, `src/SeedVR2.cs`,
`src/VisionNext.cs`, `src/CustomAi.cs`, `src/Engine.cs`.

## What goes in the new Scale window

- Video/Image output resolution (separate preset pickers, preserving original values).
- FFmpeg traditional/Shader Scale: FSR 1/2/3, NIS, SGSR, Anime4K,
  xBR/hqx/Integer, EWA/Lanczos/Bicubic/Spline, and related controls.
- OptiScaler: supersampling multiplier, RCAS and downscaling kernel.
- SeedVR2 AI restoration/upscaling.
- VisionNext and RazorNext AI upscaling.
- NVIDIA RTX AI: VSR, DLSS, and its associated TrueHDR color stage.
- Python/ONNX custom AI model: an independent preprocessing step, which is
  **not** limited to upscaling (can also restore/denoise).
- On the main settings sidebar, just one **Open Scale / AI Upscaling…** card.

TrueHDR stays with the RTX configuration, because VSR+TrueHDR are built
as a combined RTX processing stage, not unrelated FFmpeg options.

## What is actually compatible

| Combination | This code's behavior |
| --- | --- |
| FFmpeg Scale + OptiScaler | **Yes.** `Scaling.Filter` first executes Scale at supersampled size, then the OptiScaler libplacebo RCAS/downscale stage. |
| OptiScaler alone | **Yes.** Chooses EWA Lanczos Sharp for the initial pass. |
| Resolution + any output route | **Selectable.** A size request, not another algorithm. The primary route's existing encoder/scaler may apply it as a final resize; some engines have their own native size constraints. |
| RTX VSR + TrueHDR | **Yes in NVIDIA RTX pipeline.** The driver SDK supports combined VSR/TrueHDR; this code builds a common RTX stage. Hardware validation is still required. |
| RTX DLSS + VSR or TrueHDR | **Native-filter-dependent.** This source creates a sequential native FFmpeg filter chain when the requisite filter names are detected. The driver route may skip DLSS when it cannot run. This is not a promise of real-world support. |
| Python/ONNX custom model + Scale/RTX/SeedVR2/VisionNext | **Yes as staged processing**, not in one filtergraph. `MainForm` runs the user model and re-probes an intermediate before the main output stage. Model compatibility and extra time/disk cost still matter. |
| Traditional/Opti vs SeedVR2 | **Exclusive output routes** in this version. `SeedEngine.Build` rejects active `ScaleActive`. |
| Traditional/Opti vs VisionNext/RazorNext | **Exclusive output routes**; `VisionEngine.Build` rejects `ScaleActive`. |
| Traditional/Opti vs RTX | **Exclusive output routes**; `CommandBuilder` and RTX driver plans prohibit their combination. |
| SeedVR2 vs VisionNext/RazorNext | **Exclusive output routes**; each engine rejects the other. |
| SeedVR2/VisionNext vs RTX AI | **Exclusive output routes**; each engine's build step rejects `RtxActive` (including TrueHDR). |

Checkboxes only clear incompatible routes when the user **enables** a competing
route; they do not block Scale+OptiScaler, VSR+TrueHDR, or staged custom AI.
Old inconsistent settings are *not deleted on load*: the window shows a warning
until the user chooses an intended route.

**Important distinctions:** the source treats RTX TrueHDR as part of the RTX
output engine, hence incompatible with conventional Scale/AI output engines
**in this implementation**, even though TrueHDR itself is not an upscaler.
This is an implementation limitation, not a general hardware claim.

## Wheel/focus behavior

No card gives itself focus on `MouseEnter`. A message filter dispatches
WinForms wheel input to the visible settings viewport **under the mouse
pointer**, without calling `Focus()` on any section or child. Controls that
already received focus through an actual click (combo boxes, list boxes,
sliders, numeric inputs and text editors) retain native wheel behavior while
the pointer remains on them. Open ComboBox drop-down menus also retain their
native wheel behavior. A pointer in the
main file list/log does not accidentally scroll a settings window hidden
beneath another window.

## Manual Windows validation checklist

1. Open the Scale tool window, scroll over individual card labels, dark
   combo boxes and blank areas. The *outer* Scale scrollbar should move,
   without changing selections or focusing a new control.
2. Mouse-wheel the main settings sidebar while the Scale window remains
   open and overlaps it; only the **topmost hovered** settings window scrolls.
3. Click a combo box and choose an option, then close/reopen the Scale
   window. The setting remains selected and export snapshots read it.
4. Check Scale then OptiScaler: both stay on. Check SeedVR2: Scale and
   OptiScaler switch off. Check VisionNext: SeedVR2 switches off.
5. Check RTX VSR then TrueHDR: both remain on. Enable Custom Python/ONNX
   while RTX stays selected; it remains a separate preprocessing stage.
6. Uncheck `Use my AI model…`: its parameter rows collapse. Recheck
   it and confirm the rows return.
7. Switch Video/Image/Audio categories. Video/Image get separate output
   resolution pickers, Image has RTX VSR but not TrueHDR/DLSS, and Audio
   disables the Scale button.
8. Test real conversions with **your FFmpeg and GPU** before enabling a
   combination as production default. CI smoke tests validate UI logic
   and layout, not full vendor model inference.

This change does not publish, merge, or modify the existing v2.3.1 Release.
