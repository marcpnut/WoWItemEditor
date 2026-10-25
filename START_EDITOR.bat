@echo off
cd /d "%~dp0"
dotnet run --project src\WoWItemEditor\WoWItemEditor.csproj -c Release
