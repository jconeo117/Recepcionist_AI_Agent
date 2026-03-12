@echo off
color 0b
title Receptionist Agent AI Runner
echo ===================================================
echo     Receptionist Agent AI - Entorno de Desarrollo
echo ===================================================
echo.

echo [1/3] Iniciando Backend (.NET API)...
start "API Backend" cmd /k "cd src\ReceptionistAgent.Api && dotnet watch"

echo.
echo [2/3] Iniciando Admin Panel (Puerto 5173)...
start "Admin Panel" cmd /k "cd src\ReceptionistAgent.AdminPanel && npm run dev"

echo.
echo [3/3] Iniciando Client Dashboard (Puerto 5174)...
start "Client Dashboard" cmd /k "cd src\ReceptionistAgent.ClientDashboard && npm run dev"

echo.
echo ===================================================
set /p ngrok_prompt="Desea exponer el backend con ngrok para pruebas de Twilio/Meta? (S/N): "

if /I "%ngrok_prompt%"=="S" (
    echo.
    echo Iniciando ngrok en el puerto 5083...
    start "Ngrok Tunnel" cmd /k "ngrok http 5083"
    echo.
    echo [!] IMPORTANTE: Recuerde actualizar el Webhook de Twilio/Meta con la nueva URL.
) else (
    echo.
    echo Omitiendo ngrok. Ejecucion estrictamente local.
)

echo.
echo Todos los servicios han sido lanzados en ventanas separadas.
pause
