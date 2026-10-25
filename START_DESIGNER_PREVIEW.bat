@echo off
cd /d "%~dp0"
if not exist "artifacts\sharing-final\Release\net10.0-windows\WoWItemEditor.dll" (
  dotnet build -c Release -p:BaseOutputPath=artifacts/sharing-final/
  if errorlevel 1 exit /b 1
)
dotnet "artifacts\sharing-final\Release\net10.0-windows\WoWItemEditor.dll"
