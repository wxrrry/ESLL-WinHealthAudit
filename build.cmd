@echo off
setlocal enabledelayedexpansion

rem Builds the executables with the C# compiler that ships inside Windows.
rem No .NET SDK, no NuGet, no Visual Studio required.

set "ROOT=%~dp0"
set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo C# compiler not found. Install the .NET Framework 4.x developer pack.
    exit /b 1
)

if not exist "%ROOT%bin" mkdir "%ROOT%bin"

rem --- shared sources: engine, checks, helpers ---------------------------------
set "SOURCES="
for %%f in ("%ROOT%src\*.cs") do set "SOURCES=!SOURCES! "%%f""
for /r "%ROOT%src\Checks" %%f in (*.cs) do set "SOURCES=!SOURCES! "%%f""
for /r "%ROOT%src\Helpers" %%f in (*.cs) do set "SOURCES=!SOURCES! "%%f""

echo [1/2] WinHealthAudit.Core.exe (engine)...
"%CSC%" /nologo /target:exe /platform:anycpu /optimize+ /warn:4 ^
    /main:WinHealthAudit.Program ^
    /out:"%ROOT%bin\WinHealthAudit.Core.exe" ^
    /win32icon:"%ROOT%gui\logo.ico" ^
    /r:System.dll ^
    /r:System.Core.dll ^
    /r:System.Management.dll ^
    /r:System.ServiceProcess.dll ^
    /r:System.Xml.dll ^
    !SOURCES!

if errorlevel 1 (
    echo.
    echo Build failed.
    exit /b 1
)

rem --- window launcher: one exe with everything inside ----------------------------
echo [2/2] WinHealthAudit.exe (the app)...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 ^
    /main:WinHealthAudit.Window.Launcher ^
    /out:"%ROOT%bin\WinHealthAudit.exe" ^
    /win32icon:"%ROOT%gui\logo.ico" ^
    /r:System.dll ^
    /r:System.Core.dll ^
    /res:"%ROOT%gui\app.ps1",app.ps1 ^
    /res:"%ROOT%gui\index.html",index.html ^
    /res:"%ROOT%gui\logo.png",logo.png ^
    /res:"%ROOT%bin\WinHealthAudit.Core.exe",WinHealthAudit.Core.exe ^
    "%ROOT%src\Launcher\Program.cs"

if errorlevel 1 (
    echo.
    echo Build failed.
    exit /b 1
)

:done
echo.
echo Built: bin\
dir /b "%ROOT%bin\*.exe"
endlocal
