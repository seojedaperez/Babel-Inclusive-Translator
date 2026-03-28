Write-Host "Starting Inclusive Communication Hub components..." -ForegroundColor Cyan

# 1. Start the API
Write-Host "1. Starting Backend API..." -ForegroundColor Yellow
Start-Process "dotnet" -ArgumentList "run", "--project", "`"$PSScriptRoot\src\ICH.API\ICH.API.csproj`""

# Wait 5 seconds to ensure API is up and listening on port 49940
Start-Sleep -Seconds 5

# 2. Start the Background Service
Write-Host "2. Starting Background Service..." -ForegroundColor Yellow
Start-Process "dotnet" -ArgumentList "run", "--project", "`"$PSScriptRoot\src\ICH.BackgroundService\ICH.BackgroundService.csproj`""

# 3. Start the WebPortal Frontend
Write-Host "3. Starting WebPortal Frontend (HTML5 UI) on port 49938..." -ForegroundColor Yellow
Start-Process "dotnet" -ArgumentList "run", "--project", "`"$PSScriptRoot\src\ICH.WebPortal\ICH.WebPortal.csproj`""

# 4. Start the MAUI Application
Write-Host "4. Starting MAUI Desktop Application (UI)..." -ForegroundColor Yellow
Start-Process "dotnet" -ArgumentList "run", "--project", "`"$PSScriptRoot\src\ICH.MauiApp\ICH.MauiApp.csproj`"", "-f", "net8.0-windows10.0.19041.0"

Write-Host "All components started successfully!" -ForegroundColor Green
