@echo off
rem Tek dosyalik, kendi runtime'ini tasiyan AlfMcp.exe uretir: AlfMcp.Release\AlfMcp.exe
cd /d "%~dp0"
rem Claude/Codex exe'yi calistiriyorsa Windows dosyayi kilitler; once calisan AlfMcp sureclerini kapat (istemci bir sonraki baslatmada yeniden acar).
taskkill /f /im AlfMcp.exe >nul 2>&1
rem Yalnizca eski derleme dosyalari silinir; workspace klasoru (kullanici verisi) asla silinmez.
if exist AlfMcp.Release del /q AlfMcp.Release\*.* >nul 2>&1
dotnet publish AlfMcp -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o AlfMcp.Release -nologo -v q
if errorlevel 1 (
    echo.
    echo BUILD FAILED.
    pause
    exit /b 1
)
echo.
echo OK: %~dp0AlfMcp.Release\AlfMcp.exe
echo Restart Claude / Codex (menu: Restart Claude) so they start the new exe.
pause
