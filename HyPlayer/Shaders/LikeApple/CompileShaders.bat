@echo off
setlocal EnableExtensions

rem Compile every entry point used by AppleMusicOriginalBackground.cs.
rem The flags match CompileShader: EnableStrictness + OptimizationLevel3.

set "SCRIPT_DIR=%~dp0"
set "SOURCE=%SCRIPT_DIR%Shaders\AppleMusicOriginalBackground.hlsl"
set "OUTPUT_DIR=%SCRIPT_DIR%Shaders\Compiled"

if not exist "%SOURCE%" (
    echo [ERROR] Shader source not found: "%SOURCE%"
    exit /b 1
)

call :FindFxc
if errorlevel 1 exit /b 1

if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"
if errorlevel 1 (
    echo [ERROR] Failed to create output directory: "%OUTPUT_DIR%"
    exit /b 1
)

echo Using FXC: "%FXC%"
echo Source:    "%SOURCE%"
echo Output:    "%OUTPUT_DIR%"
echo.

call :Compile RotationVertex          vs_5_0 || exit /b 1
call :Compile ArtworkFillVertex       vs_5_0 || exit /b 1
call :Compile FullscreenVertex        vs_5_0 || exit /b 1
call :Compile PinchVertex             vs_5_0 || exit /b 1
call :Compile RotationPixel           ps_5_0 || exit /b 1
call :Compile BlurHorizontalPixel     ps_5_0 || exit /b 1
call :Compile BlurVerticalPixel       ps_5_0 || exit /b 1
call :Compile OrdinaryMaterialPixel   ps_5_0 || exit /b 1
call :Compile MaterialTreatedPixel    ps_5_0 || exit /b 1
call :Compile MaterialCompositePixel  ps_5_0 || exit /b 1
call :Compile PinchPixel              ps_5_0 || exit /b 1
call :Compile PinchCompositePixel     ps_5_0 || exit /b 1

echo.
echo [OK] All shaders compiled successfully.
exit /b 0

:Compile
set "ENTRY_POINT=%~1"
set "TARGET=%~2"
set "OUTPUT=%OUTPUT_DIR%\%ENTRY_POINT%.bin"

echo [%TARGET%] %ENTRY_POINT%
"%FXC%" /nologo /Ges /O3 /T "%TARGET%" /E "%ENTRY_POINT%" /Fo "%OUTPUT%" "%SOURCE%"
if errorlevel 1 (
    echo [ERROR] Failed to compile %ENTRY_POINT% as %TARGET%.
    exit /b 1
)
exit /b 0

:FindFxc
set "FXC="
set "KITS_ROOT="

for /f "delims=" %%F in ('where fxc.exe 2^>nul') do if not defined FXC set "FXC=%%F"
if defined FXC exit /b 0

if defined WindowsSdkVerBinPath call :UseFxc "%WindowsSdkVerBinPath%x64\fxc.exe"
if defined FXC exit /b 0

for /f "tokens=2,*" %%A in ('reg query "HKLM\SOFTWARE\Microsoft\Windows Kits\Installed Roots" /v KitsRoot10 2^>nul ^| findstr /i "KitsRoot10"') do set "KITS_ROOT=%%B"
if defined KITS_ROOT call :FindFxcInSdk
if defined FXC exit /b 0

echo [ERROR] fxc.exe was not found.
echo Install the Windows SDK or run this script from a Developer Command Prompt.
exit /b 1

:FindFxcInSdk
for /f "delims=" %%V in ('dir /b /ad /o-n "%KITS_ROOT%bin" 2^>nul') do if not defined FXC call :UseFxc "%KITS_ROOT%bin\%%V\x64\fxc.exe"
exit /b 0

:UseFxc
if exist "%~1" set "FXC=%~1"
exit /b 0
