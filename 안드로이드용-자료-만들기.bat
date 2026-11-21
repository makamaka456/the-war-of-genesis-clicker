@echo off
chcp 65001 >nul
rem 창세기전3 리메이크 안드로이드판 - 폰에 넣을 자료 묶음(zip 하나) 만들기. 본체는 make-data-zip.ps1
set "PS1=%~dp0make-data-zip.ps1"
if not exist "%PS1%" set "PS1=%~dp0WarOfGenesis.Android\pc-script\make-data-zip.ps1"
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%"
echo.
pause
