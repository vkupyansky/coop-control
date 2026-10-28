@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"
if not exist server.pid goto stopped
set /p PID=<server.pid
call :is_server "!PID!" || goto stale
taskkill /PID !PID! /F >nul
del server.pid 2>nul
echo Server stopped. Garage and favourites are saved on every change, so nothing is lost.
goto end
:stale
del server.pid 2>nul
:stopped
echo The server is not running.
:end
timeout /t 5
exit /b 0

rem A stale server.pid may now belong to another program: only a process
rem running RageCoop.Server.dll is stopped.
rem Without PowerShell the process name is checked as before.
:is_server
set "SERVER_CHECK="
for /f %%R in ('powershell -NoProfile -NonInteractive -Command "$p = Get-CimInstance Win32_Process -Filter 'ProcessId=%~1'; if (-not $p) { 'none' } elseif ($p.CommandLine -like '*RageCoop.Server.dll*') { 'server' } else { 'other' }" 2^>nul') do set "SERVER_CHECK=%%R"
if "%SERVER_CHECK%"=="server" exit /b 0
if "%SERVER_CHECK%"=="other" exit /b 1
if "%SERVER_CHECK%"=="none" exit /b 1
tasklist /FI "PID eq %~1" /NH 2>nul | find /I "dotnet" >nul
exit /b %errorlevel%
