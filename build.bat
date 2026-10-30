@echo off
setlocal

set GAME_PLUGINS=I:\SteamLibrary\steamapps\common\Flipping is Hard\BepInEx\plugins\OfficialSpeedrunMod

echo Building OfficialSpeedrunMod...
dotnet build -c Release

if %ERRORLEVEL% NEQ 0 (
    echo BUILD FAILED
    pause
    exit /b 1
)

echo.
echo Deploying to game...
if not exist "%GAME_PLUGINS%" mkdir "%GAME_PLUGINS%"
copy /Y "bin\Release\net6.0\OfficialSpeedrunMod.dll" "%GAME_PLUGINS%\OfficialSpeedrunMod.dll"

echo.
echo Done! Plugin deployed to:
echo %GAME_PLUGINS%
pause
