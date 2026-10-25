@echo off
cd /d "%~dp0"
if not exist "artifacts\quest-author-ready\WoWItemEditor.dll" (
  dotnet build src\WoWItemEditor\WoWItemEditor.csproj -c Release -o artifacts/quest-author-ready
  if errorlevel 1 exit /b 1
)
dotnet "artifacts\quest-author-ready\WoWItemEditor.dll"
