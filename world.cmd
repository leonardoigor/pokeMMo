@echo off
setlocal enabledelayedexpansion

rem Uso:
rem   world up <nome> <minX> <maxX> <minY> <maxY> [east] [west] [north] [south] [--skip-build]
rem   world down <nome>
rem
rem Exemplo (quadrado nas coordenadas negativas):
rem   world up square1 -9 -5 -9 -5 "" "" "" ""

set ACTION=%1
if "%ACTION%"=="" goto UPJSON
if /i "%ACTION%"=="up" if "%~2"=="" goto UPJSON
if /i "%ACTION%"=="up" if /i "%~2"=="--skip-build" (
  set OPT=--skip-build
  goto UPJSON
)
if /i "%ACTION%"=="down" if "%~2"=="" goto DOWNJSON
if /i "%ACTION%"=="up-json" goto UPJSON
if /i "%ACTION%"=="down-json" goto DOWNJSON
shift
set NAME=%1
if "%NAME%"=="" (
  echo Nome da regiao nao informado.
  exit /b 1
)
shift
set MINX=%1
shift
set MAXX=%1
shift
set MINY=%1
shift
set MAXY=%1
shift
set EAST=%1
shift
set WEST=%1
shift
set NORTH=%1
shift
set SOUTH=%1
shift
set OPT=%1

where kubectl >nul 2>&1
if errorlevel 1 (
  echo kubectl nao encontrado no PATH.
  exit /b 1
)

goto CONTINUE

:UPJSON
if "%FILE%"=="" set FILE=%2
if "%OPT%"=="" set OPT=%3
if "%FILE%"=="" set FILE=%~dp0world.regions.json
if not exist "%FILE%" (
  echo JSON de regioes nao encontrado: %FILE%
  echo Informe o caminho: %~n0 up-json ^<caminho-do-json^> [--skip-build]
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\world_json.ps1" -File "%FILE%" -Mode up -Opt "%OPT%"
exit /b 0

:DOWNJSON
if "%FILE%"=="" set FILE=%2
if "%FILE%"=="" set FILE=%~dp0world.regions.json
if not exist "%FILE%" (
  echo JSON de regioes nao encontrado: %FILE%
  echo Informe o caminho: %~n0 down-json ^<caminho-do-json^>
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\world_json.ps1" -File "%FILE%" -Mode down
exit /b 0

:CONTINUE

if /i "%ACTION%"=="up" goto DO_UP

if /i "%ACTION%"=="down" goto DO_DOWN

echo Acao desconhecida: %ACTION%
exit /b 1

:DO_UP
if /i not "%OPT%"=="--skip-build" (
  where docker >nul 2>&1
  if errorlevel 1 (
    echo docker nao encontrado no PATH.
    exit /b 1
  )
  echo Construindo e enviando imagem do microservico World...
  if exist "World\Dockerfile" (
    set IMAGE=igormendonca/world:latest
    echo - docker build -t !IMAGE! -f World\Dockerfile .
    docker build -t !IMAGE! -f World\Dockerfile .
    if errorlevel 1 exit /b !errorlevel!
    echo - docker push !IMAGE!
    docker push !IMAGE!
    if errorlevel 1 exit /b !errorlevel!
  ) else (
    echo Dockerfile do World nao encontrado em World\Dockerfile
  )
) else (
  echo Pulando build/push de imagem (--skip-build).
)

set DPLY=world-%NAME%
set SVC=world-%NAME%
set NS=creature-realms

set TMPFILE=%TEMP%\world_%NAME%.yaml
if exist "!TMPFILE!" del /f /q "!TMPFILE!" >nul 2>&1

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\\world_apply.ps1" -Name "%NAME%" -Namespace "%NS%" -Image "igormendonca/world:latest" -MinX %MINX% -MaxX %MAXX% -MinY %MINY% -MaxY %MAXY% -East "%EAST%" -West "%WEST%" -North "%NORTH%" -South "%SOUTH%"
exit /b %errorlevel%

:DO_DOWN
set DPLY=world-%NAME%
set SVC=world-%NAME%
set NS=creature-realms
echo Removendo recursos: %DPLY% e %SVC% ...
kubectl delete deployment %DPLY% -n %NS% --ignore-not-found
kubectl delete service %SVC% -n %NS% --ignore-not-found
exit /b 0
