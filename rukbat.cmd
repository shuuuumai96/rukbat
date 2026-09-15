@echo off
dotnet run --no-restore --project "%~dp0src\Rukbat\Rukbat.csproj" -- %*
exit /b %errorlevel%
