@echo off
cd /d "%~dp0"
if not exist "artifacts\recovery-complete\WoWItemEditor.dll" (
  dotnet build src\WoWItemEditor\WoWItemEditor.csproj -c Release -o artifacts/recovery-complete
  if errorlevel 1 exit /b 1
)
dotnet "artifacts\recovery-complete\WoWItemEditor.dll"
