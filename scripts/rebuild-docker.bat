@echo off
REM Reconstruye las imagenes del monolito (Iam) y Trip-Tracking y levanta todo el entorno.
cd /d "%~dp0.."
echo === 1/3 Construyendo imagenes (monolith, trip-tracking, gateway) ===
docker compose build --no-cache monolith trip-tracking gateway || goto :error
echo === 2/3 Levantando contenedores ===
docker compose up -d || goto :error
echo === 3/3 Prueba de humo (Iam + Trip-Tracking + Administration) ===
docker compose --profile smoke run --rm smoke
echo.
docker compose ps
pause
exit /b 0
:error
echo Fallo el paso anterior. Revisa que Docker Desktop este abierto.
pause
exit /b 1
