@echo off
chcp 65001 >nul
echo Compiling Aorus Adaptive Light...
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /t:winexe /optimize+ /platform:anycpu /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:AorusAdaptiveLight.exe AorusAmbientLight.cs
if %ERRORLEVEL% equ 0 (
    echo [SUCCESS] AorusAdaptiveLight.exe created successfully!
) else (
    echo [ERROR] Compilation failed.
)
pause
