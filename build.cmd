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

rem --- package gui bundle ---------------------------------------------------
echo [2/3] Packaging gui bundle...
if exist "%ROOT%bin\gui.zip" del /f /q "%ROOT%bin\gui.zip"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; [System.IO.Compression.ZipFile]::CreateFromDirectory('%ROOT%gui', '%ROOT%bin\gui.zip')"
if errorlevel 1 (
    echo.
    echo GUI bundle packaging failed.
    exit /b 1
)

rem --- window launcher: one exe with everything inside ----------------------------
echo [3/4] WinHealthAudit.exe (standalone WebView2 app)...
"%CSC%" /nologo /target:winexe /platform:x86 /optimize+ /warn:4 ^
    /main:WinHealthAudit.Window.Launcher ^
    /out:"%ROOT%bin\WinHealthAudit.exe" ^
    /win32icon:"%ROOT%gui\logo.ico" ^
    /r:System.dll ^
    /r:System.Core.dll ^
    /r:System.Drawing.dll ^
    /r:System.Windows.Forms.dll ^
    /r:System.IO.Compression.dll ^
    /r:System.IO.Compression.FileSystem.dll ^
    /r:"%ROOT%lib\webview2\Microsoft.Web.WebView2.Core.dll" ^
    /r:"%ROOT%lib\webview2\Microsoft.Web.WebView2.WinForms.dll" ^
    /res:"%ROOT%bin\gui.zip",gui.zip ^
    /res:"%ROOT%gui\logo.ico",logo.ico ^
    /res:"%ROOT%bin\WinHealthAudit.Core.exe",WinHealthAudit.Core.exe ^
    /res:"%ROOT%lib\webview2\Microsoft.Web.WebView2.Core.dll",Microsoft.Web.WebView2.Core.dll ^
    /res:"%ROOT%lib\webview2\Microsoft.Web.WebView2.WinForms.dll",Microsoft.Web.WebView2.WinForms.dll ^
    /res:"%ROOT%lib\webview2\WebView2Loader.dll",WebView2Loader.dll ^
    "%ROOT%src\Launcher\Program.cs"

if errorlevel 1 (
    echo.
    echo Build failed.
    exit /b 1
)

rem --- setup installer: embeds both WinHealthAudit.exe and WinHealthAudit.Core.exe
echo [4/4] WinHealthAudit-Setup.exe (installer)...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 ^
    /main:WinHealthAudit.Installer.Program ^
    /out:"%ROOT%bin\WinHealthAudit-Setup.exe" ^
    /win32icon:"%ROOT%gui\logo.ico" ^
    /r:System.dll ^
    /r:System.Core.dll ^
    /r:System.Drawing.dll ^
    /r:System.Windows.Forms.dll ^
    /r:Microsoft.CSharp.dll ^
    /res:"%ROOT%gui\logo.ico",logo.ico ^
    /res:"%ROOT%bin\WinHealthAudit.exe",WinHealthAudit.exe ^
    /res:"%ROOT%bin\WinHealthAudit.Core.exe",WinHealthAudit.Core.exe ^
    "%ROOT%src\Installer\Program.cs"

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
