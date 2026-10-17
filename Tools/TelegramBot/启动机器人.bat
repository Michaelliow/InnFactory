@echo off
chcp 65001 >nul
cd /d "%~dp0"
if not exist config.json (
  echo 还没有 config.json：请先把 config.example.json 复制成 config.json 并填写。
  pause
  exit /b 1
)
dotnet run -c Release
pause
