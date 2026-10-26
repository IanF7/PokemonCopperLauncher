@echo off
rem Builds dist\Launcher.exe using the C# compiler that comes with Windows.
rem Just double-click this file (or run it from a terminal).
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Couldn't find the .NET Framework C# compiler at %CSC%
  exit /b 1
)
if not exist dist mkdir dist
"%CSC%" /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ ^
  /out:dist\Launcher.exe ^
  /win32icon:assets\icon.ico ^
  /resource:assets\banner.png,banner.png ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.IO.Compression.dll ^
  src\*.cs
if errorlevel 1 (
  echo.
  echo BUILD FAILED
  exit /b 1
)
echo Built dist\Launcher.exe
