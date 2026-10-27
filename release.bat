@echo off
rem Makes everything for a new release. Double-click it, or run e.g.:
rem   release.bat 2.0.0
rem Your game folder isn't changed. Files to upload end up in releases\v<version>\upload\
setlocal
cd /d "%~dp0"
set VERSION=%~1
if "%VERSION%"=="" (
  set /p VERSION=Which version are you releasing? ^(e.g. 2.0.0^):
)
if "%VERSION%"=="" goto :fail

python tools\release.py %VERSION% %2 %3 %4 %5 %6 %7 %8
if errorlevel 1 goto :fail
echo.
pause
exit /b 0

:fail
echo.
echo Something went wrong, see the messages above. Nothing was uploaded.
pause
exit /b 1
