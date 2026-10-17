@echo off
rem Builds bin\DpiOverride.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist bin mkdir bin
"%CSC%" /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ ^
  /win32icon:assets\app.ico ^
  /resource:assets\app.ico,app.ico /resource:assets\logo.png,logo.png ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll ^
  /out:bin\DpiOverride.exe src\*.cs
if errorlevel 1 exit /b 1
rem the Windows 11 context menu DLL (skipped when the C++ build tools aren't installed)
call "%~dp0build-shellext.cmd"
