@echo off
rem Starts the ChessFixer backend (which also serves the frontend) and opens it in Firefox.
cd /d "%~dp0"

set ASPNETCORE_ENVIRONMENT=Development
start "ChessFixer server" dotnet run --project src\ChessFixer.Server --no-launch-profile --urls http://localhost:5016

timeout /t 5 /nobreak >nul
start firefox http://localhost:5016
