@echo off
rem Builds the Community Edition. Set GT_DIR to your Gorilla Tag folder (with BepInEx installed).
rem Output: out\community\board and out\community\companion
setlocal
if "%GT_DIR%"=="" (echo Set GT_DIR to your Gorilla Tag folder, e.g. set GT_DIR=C:\Program Files ^(x86^)\Steam\steamapps\common\Gorilla Tag& exit /b 1)
cd /d "%~dp0.."
if exist out\community rmdir /s /q out\community
dotnet build board\community -c Release -nologo -v q -p:GameDir="%GT_DIR%" -o out\community\board || exit /b 1
dotnet publish comp\community -c Release -nologo -v q -o out\community\companion || exit /b 1
certutil -hashfile out\community\board\UtilityBoardCommunity.dll SHA256 | findstr /v :
