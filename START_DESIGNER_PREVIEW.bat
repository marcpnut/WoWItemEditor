@echo off
cd /d "%~dp0"
if not exist "artifacts\quest-rewards-ready\WoWItemEditor.dll" (
  dotnet build src\WoWItemEditor\WoWItemEditor.csproj -c Release -o artifacts/quest-rewards-ready
  if errorlevel 1 exit /b 1
)
dotnet "artifacts\quest-rewards-ready\WoWItemEditor.dll"
