@echo off
cd /d "%~dp0"
if not exist "artifacts\weapon-display-ready\Release\net10.0-windows\AnoWowItemEditor.dll" (
  dotnet build -c Release -p:BaseOutputPath=artifacts/weapon-display-ready/
  if errorlevel 1 exit /b 1
)
dotnet "artifacts\weapon-display-ready\Release\net10.0-windows\AnoWowItemEditor.dll"
