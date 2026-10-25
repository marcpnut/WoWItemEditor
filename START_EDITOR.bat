@echo off
cd /d "%~dp0"
dotnet run --project WoWItemEditor.csproj -c Release
