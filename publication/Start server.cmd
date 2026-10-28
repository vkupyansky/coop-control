@echo off
setlocal EnableDelayedExpansion
title OG Coop server
cd /d "%~dp0"
for %%F in (RageCoop.Server.dll Bootstrap\CoopAdmin.Startup.dll Bootstrap\0Harmony.dll) do (
  if not exist "%%F" (
    echo Missing %%F. Extract the complete server folder again.
    pause
    exit /b 1
  )
)
if exist server.pid (
  set /p PID=<server.pid
  call :is_server "!PID!" && (
    echo The server is already running. To stop it, run "Stop server".
    pause
    exit /b 0
  )
)
rem The .NET runtime shipped beside the server needs no installation.
set "DOTNET=dotnet"
if exist "%~dp0dotnet\dotnet.exe" (
  set "DOTNET=%~dp0dotnet\dotnet.exe"
  set "DOTNET_ROOT=%~dp0dotnet"
) else (
  where dotnet >nul 2>&1 || (
    echo Install the .NET 10 Runtime x64: https://dotnet.microsoft.com/download/dotnet/10.0
    pause
    exit /b 1
  )
)
set "DOTNET_STARTUP_HOOKS=%~dp0Bootstrap\CoopAdmin.Startup.dll"
set "DOTNET_ROLL_FORWARD=Major"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
"%DOTNET%" RageCoop.Server.dll
echo.
echo The server has stopped.
pause
exit /b 0

rem A stale server.pid may now belong to another program: only a process
rem running RageCoop.Server.dll counts as the server.
rem Without PowerShell the process name is checked as before.
:is_server
set "SERVER_CHECK="
for /f %%R in ('powershell -NoProfile -NonInteractive -Command "$p = Get-CimInstance Win32_Process -Filter 'ProcessId=%~1'; if (-not $p) { 'none' } elseif ($p.CommandLine -like '*RageCoop.Server.dll*') { 'server' } else { 'other' }" 2^>nul') do set "SERVER_CHECK=%%R"
if "%SERVER_CHECK%"=="server" exit /b 0
if "%SERVER_CHECK%"=="other" exit /b 1
if "%SERVER_CHECK%"=="none" exit /b 1
tasklist /FI "PID eq %~1" /NH 2>nul | find /I "dotnet" >nul
exit /b %errorlevel%
