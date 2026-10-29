@echo off
setlocal
cd /d "%~dp0"
if not exist "RopeShelf.exe" (
  echo Extract the complete portable package first.
  pause
  exit /b 1
)
"%~dp0RopeShelf.exe" --data-root "%~dp0ManualTestData"
if errorlevel 1 pause
