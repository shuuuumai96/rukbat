@echo off
dotnet run --no-restore --project "%~dp0src\Rukbat\Rukbat.csproj" -- build %*
exit /b %errorlevel%
