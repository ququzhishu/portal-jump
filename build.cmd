@echo off
chcp 65001 >nul
rem =====================================================================
rem  折返跃迁 / Portal Jump build wrapper
rem  Usage:  build.cmd            (build in place -> .\PortalJump\)
rem          build.cmd -Dist      (also create dist\PortalJump-<ver>\)
rem          build.cmd -Dist -Zip (also create a zip)
rem  Double-clicking this file builds in place.
rem =====================================================================
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
set RC=%ERRORLEVEL%
if not "%RC%"=="0" (
    echo.
    echo Build failed with exit code %RC%.
    pause
) else (
    echo.
    echo Build finished.
    ping -n 4 127.0.0.1 >nul
)
exit /b %RC%
