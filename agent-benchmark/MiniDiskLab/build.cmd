@echo off
REM ============================================================
REM  MiniDiskLab build helper
REM  Works around the broken HOME/PROGRAMDATA env vars in the
REM  agent shell, which make NuGet's machine-wide settings
REM  lookup throw "Value cannot be null. (Parameter 'path1')".
REM ============================================================
setlocal

set "REPO_ROOT=%~dp0.."
set "DOTNET=%REPO_ROOT%\..\..\dotnet8sdk\dotnet.exe"

set "ProgramData=C:\ProgramData"
set "ALLUSERSPROFILE=C:\ProgramData"
set "APPDATA=C:\Users\Administrator.DESKTOP-RHFCBBR\AppData\Roaming"
set "LOCALAPPDATA=C:\Users\Administrator.DESKTOP-RHFCBBR\AppData\Local"
set "USERPROFILE=C:\Users\Administrator.DESKTOP-RHFCBBR"
set "HOMEDRIVE=C:"
set "HOMEPATH=\Users\Administrator.DESKTOP-RHFCBBR"
set "HOME=C:\Users\Administrator.DESKTOP-RHFCBBR"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
set "NUGET_PACKAGES=C:\nugetcache"

cd /d "%~dp0"
"%DOTNET%" %*
exit /b %ERRORLEVEL%
