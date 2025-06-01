@echo off

for /f "usebackq tokens=*" %%i in (monolog.txt) do (SET WORLD=%%i)
for %%i in ("%WORLD%") do (
    set DIR=%%~pi
    set MIS=%%~nxi
)

set COUNT=0
set DIR2=%DIR%

:loopprocess
for /F "tokens=1* delims=\\" %%A in ( "%DIR2%" ) do (
  set /A COUNT+=1
  set DIR2=%%B
  goto loopprocess
)

for /F "tokens=%COUNT% delims=\\" %%A in ( "%DIR%" ) do set FM=%%A

echo Lighting %FM%/%MIS%
Tools\KCTools\KCTools.exe light . "%MIS%" -c "%FM%" -q
