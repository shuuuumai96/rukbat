@echo off
dotnet run --no-restore --project "%~dp0src\Auvil\Auvil.csproj" -- build %*
exit /b %errorlevel%
