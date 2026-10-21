@echo off
rem Windows CLIENT deploy (UC-T27): pulls latest, builds the Release player only if the source changed, publishes it to
rem C:\Apps\Unity.Cursory, refreshes the Desktop shortcut, and launches it (tools\deploy.ps1 does the work).
rem Not the dedicated server: that is the root deploy.ps1 (Unity.Multiplayer's mp-host game), see docs\hosting.md.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\deploy.ps1" -Launch %*
if errorlevel 1 (
    echo.
    echo   Deploy failed. See Logs\deploy-build.log.
    pause
    exit /b 1
)
timeout /t 5
