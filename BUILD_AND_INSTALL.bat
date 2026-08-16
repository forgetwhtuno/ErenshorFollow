@echo off
setlocal
cd /d "%~dp0"

echo ============================================================
echo Erenshor Follow 0.6.3 - Build and Install
echo ============================================================
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0RUN_TESTS.ps1"
if errorlevel 1 (
    echo.
    echo ============================================================
    echo FOLLOW 0.6.3 TESTS FAILED - NOTHING WAS INSTALLED
    echo ============================================================
    echo Copy the error above if you need help.
    pause
    exit /b 1
)

echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0BUILD_AND_INSTALL.ps1"
if errorlevel 1 (
    echo.
    echo ============================================================
    echo FOLLOW 0.6.3 BUILD OR INSTALL FAILED
    echo ============================================================
    echo Copy the error above if you need help.
    pause
    exit /b 1
)

echo.
echo ============================================================
echo FOLLOW 0.6.3 INSTALLED SUCCESSFULLY
echo ============================================================
echo Expected live startup marker:
echo   Erenshor Follow 0.6.3 loaded. Sim Actions retained UI revision=2
echo.
echo In game, run:
echo   /efollow ui
echo to verify the custom Follow SIM ACTIONS surface revision.
echo.
echo No Git operations were performed.
echo.
pause
exit /b 0
