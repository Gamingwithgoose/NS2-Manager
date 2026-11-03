@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title NS2 Manager - Build
echo ========================================
echo        NS2 Manager - Windows Build
echo ========================================
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET 8 SDK was not found.
    echo Install the .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0
    echo Make sure to install the SDK, not just the runtime.
    goto :failed
)

dotnet --list-sdks | findstr /R /C:"^8\." >nul
if errorlevel 1 (
    echo ERROR: .NET 8 SDK is required.
    echo Download: https://dotnet.microsoft.com/download/dotnet/8.0
    goto :failed
)

set "PROJECT=src\NS2Manager\NS2Manager.csproj"
set "OUTDIR=%~dp0dist\win-x64"
if not exist "%PROJECT%" (
    echo ERROR: Project file missing: %PROJECT%
    goto :failed
)

echo Building self-contained Windows x64 application...
dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "%OUTDIR%"
if errorlevel 1 goto :failed

if not exist "%OUTDIR%\NS2Manager.exe" (
    echo ERROR: Build returned success, but NS2Manager.exe was not found.
    goto :failed
)
echo.
echo ========================================
echo BUILD SUCCESSFUL
echo EXE: "%OUTDIR%\NS2Manager.exe"
echo ========================================
echo.
pause
exit /b 0

:failed
echo.
echo ========================================
echo BUILD FAILED - review errors above.
echo ========================================
echo.
pause
exit /b 1
