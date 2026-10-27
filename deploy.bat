@echo off
rem Windows CLIENT launcher (UC-T27); the Desktop shortcut runs it. It asks first (MAT-T67):
rem   1) Deploy & Run: tools\deploy.ps1 -Force -Launch pulls latest, rebuilds the Release player, publishes it to
rem      C:\Apps\Unity.Cursory, refreshes the Desktop shortcut and launches it.
rem   2) Run: launches the published C:\Apps\Unity.Cursory\Unity.Cursory.exe without deploying (deploys if missing).
rem Not the dedicated server: that is the root deploy.ps1 (Unity.Multiplayer's mp-host game), see docs\hosting.md.
rem No goto/labels on purpose: this file is LF (HOUSE-LAW-13), and cmd's label search is unreliable in LF files.
setlocal
set "APP_DIR=C:\Apps\Unity.Cursory"
set "APP_EXE=%APP_DIR%\Unity.Cursory.exe"
echo.
echo   Unity.Cursory
echo     1^) Deploy ^& Run
echo     2^) Run
echo.
choice /c 12 /n /m "  Choose 1 or 2: "
set "PICK=%ERRORLEVEL%"
if "%PICK%"=="2" if not exist "%APP_EXE%" (
    echo   Nothing is published at %APP_DIR% yet - deploying first.
    set "PICK=1"
)
if "%PICK%"=="2" (
    start "" /d "%APP_DIR%" "%APP_EXE%"
    exit /b 0
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\deploy.ps1" -Force -Launch %*
if errorlevel 1 (
    echo.
    echo   Deploy failed. See Logs\deploy-build.log.
    pause
    exit /b 1
)
timeout /t 5
