@echo off
cd /d "%~dp0"
if not exist "artifacts\set-refresh\Release\net10.0-windows\AnoWowItemEditor.dll" (
  dotnet build -c Release -p:BaseOutputPath=artifacts/set-refresh/
  if errorlevel 1 exit /b 1
)
dotnet "artifacts\set-refresh\Release\net10.0-windows\AnoWowItemEditor.dll"
