@echo off
cd /d "%~dp0"
if not exist "artifacts\armory-ready\Release\net10.0-windows\AnoWowItemEditor.dll" (
  dotnet build -c Release -p:BaseOutputPath=artifacts/armory-ready/
  if errorlevel 1 exit /b 1
)
dotnet "artifacts\armory-ready\Release\net10.0-windows\AnoWowItemEditor.dll"
