@echo off
cd /d "%~dp0"
if not exist "artifacts\set-checklist\Release\net10.0-windows\AnoWowItemEditor.dll" (
  dotnet build -c Release -p:BaseOutputPath=artifacts/set-checklist/
  if errorlevel 1 exit /b 1
)
dotnet "artifacts\set-checklist\Release\net10.0-windows\AnoWowItemEditor.dll"
