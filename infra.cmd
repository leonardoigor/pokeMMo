@echo off
setlocal enabledelayedexpansion

set BASE=k8s
if not exist "%BASE%" (
  echo Pasta %BASE% nao existe.
  exit /b 1
)

set ACTION=%1
if "%ACTION%"=="" set ACTION=up
set OPT=%2

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

if /i "%OP%"=="apply" (
  where docker >nul 2>&1
  if errorlevel 1 (
    echo docker nao encontrado no PATH.
    exit /b 1
  )
  echo Aplicando namespace...
  kubectl apply -f "%BASE%\\namespace.yaml"
  if errorlevel 1 exit /b !errorlevel!
  if /i not "%OPT%"=="--skip-build" if /i not "%OPT%"=="fast" (
    echo Construindo e enviando imagem do microservico Auth...
    if exist "Auth\\Dockerfile" (
      set IMAGE=igormendonca/auth:latest
      echo - docker build -t !IMAGE! -f Auth\\Dockerfile .
      docker build -t !IMAGE! -f Auth\\Dockerfile .
      if errorlevel 1 exit /b !errorlevel!
      echo - docker push !IMAGE!
      docker push !IMAGE!
      if errorlevel 1 exit /b !errorlevel!
    ) else (
      echo Dockerfile do Auth nao encontrado em Auth\\Dockerfile
    )
    echo Construindo e enviando imagem do microservico Users...
    if exist "Users\\Dockerfile" (
      set IMAGE=igormendonca/users:latest
      echo - docker build -t !IMAGE! -f Users\\Dockerfile .
      docker build -t !IMAGE! -f Users\\Dockerfile .
      if errorlevel 1 exit /b !errorlevel!
      echo - docker push !IMAGE!
      docker push !IMAGE!
      if errorlevel 1 exit /b !errorlevel!
    ) else (
      echo Dockerfile do Users nao encontrado em Users\\Dockerfile
    )
  ) else (
    echo Modo rapido: pulando build/push de imagem.
  )
)

if exist "%BASE%\\kustomization.yaml" (
  echo Executando kubectl %OP% -k "%BASE%"
  kubectl %OP% -k "%BASE%"
  if errorlevel 1 exit /b !errorlevel!
  if /i "%OP%"=="apply" (
    echo Forcando atualizacao dos workloads...
    kubectl rollout restart deployment auth -n creature-realms
    kubectl rollout restart deployment users -n creature-realms
    kubectl rollout restart deployment gateway -n creature-realms
    kubectl rollout restart deployment otel-collector -n creature-realms
    kubectl rollout restart statefulset postgres -n creature-realms
    kubectl rollout restart statefulset redis -n creature-realms
  )
  exit /b 0
)

for /r "%BASE%" %%F in (kustomization.yaml) do (
  set "current=%%~dpF"
  if "!current:~-1!"=="\" set "current=!current:~0,-1!"
  set "root=%CD%\%BASE%"
  if "!root:~-1!"=="\" set "root=!root:~0,-1!"
  if /i not "!current!"=="!root!" (
    echo Executando kubectl %OP% -k "!current!"
    kubectl %OP% -k "!current!"
    if errorlevel 1 exit /b !errorlevel!
  )
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
