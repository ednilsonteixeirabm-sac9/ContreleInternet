@echo off
setlocal

set "SERVICE=ControleInternetService"
set "INSTALL=%ProgramFiles%\ControleInternet"
set "DATA=%ProgramData%\ControleInternet"
set "SERVICE_EXISTS=0"

"%SystemRoot%\System32\fltmc.exe" >nul 2>&1
if errorlevel 1 (
    echo ERRO: execute este desinstalador como administrador.
    exit /b 1
)

sc.exe query "%SERVICE%" >nul 2>&1
if not errorlevel 1 (
    set "SERVICE_EXISTS=1"
    sc.exe stop "%SERVICE%" >nul 2>&1
    call :wait_stopped
    if errorlevel 1 exit /b 1
)

if exist "%INSTALL%\ControleInternetService.exe" (
    "%INSTALL%\ControleInternetService.exe" --restore-proxy
    if errorlevel 1 (
        echo ERRO: o proxy original nao foi restaurado.
        echo A desinstalacao foi interrompida para preservar o backup.
        exit /b 1
    )
) else if exist "%DATA%\windows-backup.json" (
    echo ERRO: o executavel necessario para restaurar o proxy nao foi encontrado.
    echo A pasta de dados foi preservada em "%DATA%".
    exit /b 1
)

if "%SERVICE_EXISTS%"=="1" (
    sc.exe delete "%SERVICE%" >nul 2>&1
    if errorlevel 1 (
        echo ERRO: nao foi possivel remover o registro do servico.
        exit /b 1
    )
)

rd /S /Q "%INSTALL%" >nul 2>&1
if exist "%INSTALL%" (
    echo ERRO: nao foi possivel remover "%INSTALL%".
    exit /b 1
)

rd /S /Q "%DATA%" >nul 2>&1
if exist "%DATA%" (
    echo ERRO: nao foi possivel remover "%DATA%".
    exit /b 1
)

echo.
echo Desinstalacao concluida. A configuracao original de proxy foi restaurada.
exit /b 0

:wait_stopped
for /L %%I in (1,1,20) do (
    sc.exe query "%SERVICE%" | find "STOPPED" >nul 2>&1
    if not errorlevel 1 exit /b 0
    ping 127.0.0.1 -n 2 >nul
)
echo ERRO: o servico nao parou.
exit /b 1
