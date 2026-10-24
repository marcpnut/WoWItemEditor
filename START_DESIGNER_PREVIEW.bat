@echo off
cd /d "%~dp0"
if not exist "artifacts\safety\Release\net10.0-windows\AnoWowItemEditor.dll" (
  dotnet build -c Release -p:BaseOutputPath=artifacts/safety/
  if errorlevel 1 exit /b 1
)
dotnet "artifacts\safety\Release\net10.0-windows\AnoWowItemEditor.dll"
