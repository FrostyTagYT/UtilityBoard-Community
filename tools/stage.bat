@echo off
rem Stages a release folder from out\community. Set STAGE_DIR (default: dist\UtilityBoard-CommunityEdition).
setlocal
cd /d "%~dp0.."
if "%STAGE_DIR%"=="" set STAGE_DIR=%CD%\dist\UtilityBoard-CommunityEdition
if exist "%STAGE_DIR%" rmdir /s /q "%STAGE_DIR%"
mkdir "%STAGE_DIR%\BepInEx-plugin\UtilityBoardCommunity" "%STAGE_DIR%\UtilityBoardCompanion"
copy /y out\community\board\UtilityBoardCommunity.dll "%STAGE_DIR%\BepInEx-plugin\UtilityBoardCommunity\" >nul
xcopy /e /q /y out\community\companion "%STAGE_DIR%\UtilityBoardCompanion\" >nul
copy /y docs\INSTALL_Community.txt "%STAGE_DIR%\INSTALL.txt" >nul
(echo @echo off& echo cd /d "%%~dp0UtilityBoardCompanion"& echo UtilityBoardCompanion.exe) > "%STAGE_DIR%\Start Utility Board Companion.bat"
python tools\leakcheck.py "%STAGE_DIR%" || (echo LEAKCHECK FAILED - do not publish& exit /b 1)
