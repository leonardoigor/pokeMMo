@echo off
setlocal enabledelayedexpansion

set BASE=k8s
if not exist "%BASE%" (
  echo Pasta %BASE% nao existe.
  exit /b 1
)

set ACTION=%1
if "%ACTION%"=="" set ACTION=up

if /i "%ACTION%"=="up" set OP=apply
if /i "%ACTION%"=="down" set OP=delete
if /i "%ACTION%"=="plan" set OP=plan

if "%OP%"=="" (
  echo Uso: %~n0 [up^|down^|plan]
  exit /b 1
)

if /i "%OP%"=="plan" (
  echo Plano:
  if exist "%BASE%\kustomization.yaml" (
    echo - kustomize: %BASE%
  ) else (
    for /r "%BASE%" %%F in (kustomization.yaml) do (
      echo - kustomize: %%~dpF
    )
    for /r "%BASE%" %%F in (*.yaml) do (
      if not exist "%%~dpF\kustomization.yaml" echo - yaml: %%~fF
    )
    for /r "%BASE%" %%F in (*.yml) do (
      if not exist "%%~dpF\kustomization.yaml" echo - yaml: %%~fF
    )
  )
  exit /b 0
)

where kubectl >nul 2>&1
if errorlevel 1 (
  echo kubectl nao encontrado no PATH.
  exit /b 1
)

if exist "%BASE%\kustomization.yaml" (
  echo Executando kubectl %OP% -k "%BASE%"
  kubectl %OP% -k "%BASE%"
  exit /b %errorlevel%
)

for /r "%BASE%" %%F in (kustomization.yaml) do (
  echo Executando kubectl %OP% -k "%%~dpF"
  kubectl %OP% -k "%%~dpF"
  if errorlevel 1 exit /b !errorlevel!
)

for /r "%BASE%" %%F in (*.yaml) do (
  if not exist "%%~dpF\kustomization.yaml" (
    echo Executando kubectl %OP% -f "%%~fF"
    kubectl %OP% -f "%%~fF"
    if errorlevel 1 exit /b !errorlevel!
  )
)

for /r "%BASE%" %%F in (*.yml) do (
  if not exist "%%~dpF\kustomization.yaml" (
    echo Executando kubectl %OP% -f "%%~fF"
    kubectl %OP% -f "%%~fF"
    if errorlevel 1 exit /b !errorlevel!
  )
)

exit /b 0
