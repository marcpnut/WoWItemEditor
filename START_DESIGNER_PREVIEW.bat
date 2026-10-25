@echo off
cd /d "%~dp0"
if not exist "artifacts\quest-local\WoWItemEditor.dll" (
  dotnet build src\WoWItemEditor\WoWItemEditor.csproj -c Release -o artifacts/quest-local
  if errorlevel 1 exit /b 1
)
dotnet "artifacts\quest-local\WoWItemEditor.dll"
