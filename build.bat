@echo off
setlocal
REM Portable 2.3.3: source in src\, generated executables only in dist\.
cd /d "%~dp0"
if not exist "dist" mkdir "dist"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (echo ERROR: .NET C# compiler not found & exit /b 1)
"%CSC%" /nologo /target:winexe /optimize+ /win32icon:src\app.ico /out:dist\FFmpegForge.exe /resource:shaders\FSR.glsl,FSR.glsl /resource:shaders\NIS.glsl,NIS.glsl /resource:shaders\SGSR.glsl,SGSR.glsl /resource:shaders\Anime4K.glsl,Anime4K.glsl /resource:shaders\OptiScaler.glsl,OptiScaler.glsl /resource:shaders\FSR2.glsl,FSR2.glsl /resource:src\hrir_ircam.bin,hrir_ircam.bin /resource:aidenoise\forge_ai_denoise.py,forge_ai_denoise.py /resource:aidenoise\network_scunet.py,network_scunet.py /resource:aidenoise\LICENSE-SCUNet.txt,LICENSE-SCUNet.txt /resource:aidenoise\forge_visionnext.py,forge_visionnext.py /resource:aidenoise\forge_custom_ai.py,forge_custom_ai.py /resource:aidenoise\forge_razornext.py,forge_razornext.py /resource:qtgmc\forge_qtgmc.py,forge_qtgmc.py /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs
if errorlevel 1 (echo Build FAILED & exit /b 1)
echo OK: dist\FFmpegForge.exe
exit /b 0
