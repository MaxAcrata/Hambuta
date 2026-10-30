@echo off
rem Voice module launcher (from voice\ folder).
cd /d "%~dp0"
if not exist ".venv\Scripts\python.exe" (
  echo venv missing. Create it:
  echo   python -m venv .venv
  echo   .venv\Scripts\python.exe -m pip install -r requirements.txt
  exit /b 1
)
".venv\Scripts\python.exe" run.py %*
