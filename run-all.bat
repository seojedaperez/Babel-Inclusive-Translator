@echo off
echo Limpiando archivos .log antiguos...
del /q *.log 2>nul
echo Compilando MAUI APP...
dotnet build src\ICH.MauiApp\ICH.MauiApp.csproj -f net8.0-windows10.0.19041.0 >nul 2>&1

echo ----- Iniciando Inclusive Communication Hub -----

echo 1. Iniciando API
start "ICH.API" cmd /k "dotnet run --project src\ICH.API\ICH.API.csproj"

echo 2. Iniciando Sign Language Interpreter (sign.mt - port 4200)
start "Sign.mt" cmd /k "cd src\sign.mt && npm start"

echo Esperando 5 segundos para asegurar puerto 49940...
ping 127.0.0.1 -n 6 >nul

echo 3. Iniciando Background Service 
start "ICH.BackgroundService" cmd /k "dotnet run --project src\ICH.BackgroundService\ICH.BackgroundService.csproj"

echo 4. Iniciando WebPortal Frontend (HTML5 UI - port 49938)
start "ICH.WebPortal" cmd /k "dotnet run --project src\ICH.WebPortal\ICH.WebPortal.csproj"

echo 5. Iniciando UI App MAUI
start "ICH.MauiApp" /MAX cmd /k "dotnet run --project src\ICH.MauiApp\ICH.MauiApp.csproj -f net8.0-windows10.0.19041.0 -lp `"ICH.MauiApp (Packaged)`""

echo.
echo =========================================================
echo TODOS LOS PROCESOS EN MARCHA.
echo   - API:          https://localhost:49940
echo   - Sign.mt:      http://localhost:4200
echo   - WebPortal:    https://localhost:49938
echo   - MAUI:         Ventana nativa
echo =========================================================
pause
