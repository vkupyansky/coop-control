@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"
if not exist server.pid goto stopped
set /p PID=<server.pid
tasklist /FI "PID eq !PID!" /NH 2>nul | find /I "dotnet" >nul || goto stale
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
