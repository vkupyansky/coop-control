@echo off
setlocal EnableDelayedExpansion
title Coop Control server
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
  tasklist /FI "PID eq !PID!" /NH 2>nul | find /I "dotnet" >nul && (
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
