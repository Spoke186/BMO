@echo off
chcp 65001 > nul
cd /d "%~dp0api"
echo.
echo  ==========================================
echo      BMO OS Dashboard v1.0
echo  ==========================================
echo.
echo  Instalando dependencias...
pip install -r requirements.txt -q
echo.
echo  Iniciando servidor en http://localhost:8000
echo.
timeout /t 2 /nobreak > nul
start "" "http://localhost:8000"
C:\Users\Sergio\anaconda3\envs\Trade\python.exe -m uvicorn main:app --host 0.0.0.0 --port 8000
pause
