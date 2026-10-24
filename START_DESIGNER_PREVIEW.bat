@echo off
cd /d "%~dp0"
if not exist "artifacts\equipment\Release\net10.0-windows\AnoWowItemEditor.dll" (
  dotnet build -c Release -p:BaseOutputPath=artifacts/equipment/
  if errorlevel 1 exit /b 1
)
dotnet "artifacts\equipment\Release\net10.0-windows\AnoWowItemEditor.dll"
