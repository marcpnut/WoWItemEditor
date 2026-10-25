@echo off
cd /d "%~dp0"
if not exist "artifacts\set-character-011\Release\net10.0-windows\WoWItemEditor.dll" (
  dotnet build src\WoWItemEditor\WoWItemEditor.csproj -c Release -o artifacts/set-character-011/Release/net10.0-windows
  if errorlevel 1 exit /b 1
)
dotnet "artifacts\set-character-011\Release\net10.0-windows\WoWItemEditor.dll"
