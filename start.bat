@echo off
chcp 65001 >nul
title ReceptionistAI — Launcher
color 0A

echo.
echo  ╔══════════════════════════════════════════╗
echo  ║     ReceptionistAI — Launcher v1.0       ║
echo  ╚══════════════════════════════════════════╝
echo.

:: ─── Paths ────────────────────────────────────────────────────────
set "ROOT=%~dp0"
set "API_DIR=%ROOT%src\ReceptionistAgent.Api"
set "PANEL_DIR=%ROOT%src\ReceptionistAgent.AdminPanel"

:: ─── 1. Start Backend ─────────────────────────────────────────────
echo  [1/3] Iniciando Backend (dotnet run)...
echo        URL: http://localhost:5083
echo        Swagger: http://localhost:5083/swagger
echo.
start "ReceptionistAI-Backend" cmd /k "cd /d "%API_DIR%" && dotnet run --launch-profile http"

:: Wait a moment for the backend to begin starting
timeout /t 3 /nobreak >nul

:: ─── 2. Start Frontend ────────────────────────────────────────────
echo  [2/3] Iniciando Admin Panel (npm run dev)...
echo        URL: http://localhost:5173
echo.
start "ReceptionistAI-AdminPanel" cmd /k "cd /d "%PANEL_DIR%" && npm run dev"

timeout /t 2 /nobreak >nul

:: ─── 3. Ngrok ─────────────────────────────────────────────────────
echo.
echo  ══════════════════════════════════════════
echo   Backend:     http://localhost:5083
echo   Admin Panel: http://localhost:5173
echo  ══════════════════════════════════════════
echo.
set /p NGROK="  ¿Exponer el backend con ngrok para pruebas? (s/n): "

if /i "%NGROK%"=="s" (
    echo.
    echo  [3/3] Iniciando ngrok en puerto 5083...
    echo        La URL publica aparecera en la ventana de ngrok.
    echo.
    start "ReceptionistAI-Ngrok" cmd /k "ngrok http 5083"
    echo  ✓ ngrok iniciado. Revisa la ventana para obtener la URL pública.
) else (
    echo.
    echo  ✓ ngrok omitido.
)

echo.
echo  ╔══════════════════════════════════════════╗
echo  ║  Todo listo. Cierra esta ventana cuando  ║
echo  ║  quieras detener los servicios.          ║
echo  ╚══════════════════════════════════════════╝
echo.
pause
