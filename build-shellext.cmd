@echo off
rem Builds bin\DpiOverrideShell.dll, the "DPI scaling override..." entry in Windows 11's compact context menu
rem (shellext\DpiOverrideShell.cpp), with the Visual Studio C++ build tools. Without them it says so and stops:
rem the app works without the DLL and then only has the classic menu entry.
setlocal
cd /d "%~dp0"
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" goto novs
for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSDIR=%%i"
if not defined VSDIR goto novs
call "%VSDIR%\VC\Auxiliary\Build\vcvars64.bat" >nul 2>nul || goto novs
if not exist bin mkdir bin
if not exist obj mkdir obj
cl /nologo /utf-8 /LD /O2 /MT /EHsc /W4 /DUNICODE /D_UNICODE /Foobj\ shellext\DpiOverrideShell.cpp /link /NOLOGO /DEF:shellext\DpiOverrideShell.def /IMPLIB:obj\DpiOverrideShell.lib /OUT:bin\DpiOverrideShell.dll ole32.lib shlwapi.lib shell32.lib uuid.lib
exit /b %errorlevel%

:novs
echo Visual Studio C++ build tools not found: bin\DpiOverrideShell.dll (Windows 11 context menu) not built.
exit /b 0