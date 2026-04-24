@echo off
REM Build script for Spark installer (Windows batch)
REM This script compiles the NSIS script into a professional Windows installer

setlocal enabledelayedexpansion

if "%1"=="help" goto :showhelp
if "%1"=="/?" goto :showhelp
if "%1"=="--help" goto :showhelp

set BUILD_CONFIG=Debug
set RUNTIME_ID=win-x64
if "%1"=="release" set BUILD_CONFIG=Release
if "%1"=="Release" set BUILD_CONFIG=Release
if /I "%2"=="arm64" set RUNTIME_ID=win-arm64
if /I "%2"=="win-arm64" set RUNTIME_ID=win-arm64
if /I "%2"=="x64" set RUNTIME_ID=win-x64
if /I "%2"=="win-x64" set RUNTIME_ID=win-x64
set INSTALLER_NAME=SparkInstaller-%RUNTIME_ID%.exe
set APP_VERSION=1.0.1
for /f "tokens=2 delims=<>" %%v in ('findstr /R /C:"<Version>.*</Version>" Spark.csproj') do set APP_VERSION=%%v

echo.
echo ================================
echo Spark Installer Build Script
echo ================================
echo.
echo Version: %APP_VERSION%
echo.

REM Step 1: Publish self-contained application
echo [1/4] Publishing Spark application (%BUILD_CONFIG%, %RUNTIME_ID%, self-contained)...
dotnet publish .\Spark.csproj -c %BUILD_CONFIG% -r %RUNTIME_ID% -p:SelfContained=true -p:PublishSingleFile=false
if errorlevel 1 (
    echo ERROR: Failed to publish Spark application
    exit /b 1
)
echo Publish successful
echo.

REM Step 2: Check for NSIS
echo [2/4] Checking NSIS installation...
set NSIS_PATH=C:\Program Files (x86)\NSIS\makensis.exe
if not exist "%NSIS_PATH%" (
    set NSIS_PATH=C:\Program Files\NSIS\makensis.exe
)

if not exist "%NSIS_PATH%" (
    echo ERROR: NSIS not found!
    echo Please install NSIS from: https://nsis.sourceforge.io/
    exit /b 1
)
echo NSIS found at: %NSIS_PATH%
echo.

REM Step 3: Verify publish artifacts
echo [3/4] Verifying publish artifacts...
set BUILD_PATH=bin\%BUILD_CONFIG%\net10.0-windows\%RUNTIME_ID%\publish

if not exist "%BUILD_PATH%\Spark.exe" (
    echo ERROR: Build artifacts not found at %BUILD_PATH%
    exit /b 1
)
echo Publish artifacts verified
echo.

REM Step 4: Compile NSIS
echo [4/4] Compiling NSIS installer...
if not exist "Installer\Spark.nsi" (
    echo ERROR: NSIS script not found at Installer\Spark.nsi
    exit /b 1
)

"%NSIS_PATH%" /V2 "/DVERSION=%APP_VERSION%" "/DBUILD_CONFIG=%BUILD_CONFIG%" "/DRUNTIME_ID=%RUNTIME_ID%" "/DPUBLISH_DIR=..\bin\%BUILD_CONFIG%\net10.0-windows\%RUNTIME_ID%\publish" "/DINSTALLER_NAME=%INSTALLER_NAME%" "Installer\Spark.nsi"
if errorlevel 1 (
    echo ERROR: NSIS compilation failed
    exit /b 1
)

echo.
echo ================================
echo Installer created successfully!
echo ================================
echo.
echo Output file: %INSTALLER_NAME%
echo Installation path: C:\Program Files\Spark
echo.
echo You can now distribute %INSTALLER_NAME% to users.
echo.

goto :end

:showhelp
echo.
echo Spark Installer Build Script
echo.
echo Usage:
echo   Build-Installer.bat [options]
echo.
echo Options:
echo   release          Build with Release configuration
echo   help, /?, --help Show this help message
echo.
echo Examples:
echo   Build-Installer.bat           (builds with Debug config^)
echo   Build-Installer.bat release   (builds with Release config^)
echo   Build-Installer.bat release arm64  (builds ARM64 installer^)
echo.
echo Requirements:
echo   - NSIS 3.x installed from https://nsis.sourceforge.io/
echo   - .NET 10 SDK installed
echo.

:end
endlocal
