@echo off
setlocal

set "SERVICE=ControleInternetService"
set "INSTALL=%ProgramFiles%\ControleInternet"
set "DATA=%ProgramData%\ControleInternet"
set "SERVICE_BUILD=%~dp0..\ControleInternetService\bin\Release"
set "UI_BUILD=%~dp0..\ControleInternet\bin\Release"

"%SystemRoot%\System32\fltmc.exe" >nul 2>&1
if errorlevel 1 (
    echo ERRO: execute este instalador como administrador.
    exit /b 1
)

if not exist "%SERVICE_BUILD%\ControleInternetService.exe" (
    echo ERRO: compile a solution em Release antes de instalar.
    exit /b 1
)

sc.exe query "%SERVICE%" >nul 2>&1
if not errorlevel 1 (
    echo ERRO: o servico ja esta instalado. Desinstale-o antes de reinstalar.
    exit /b 1
)

mkdir "%INSTALL%" >nul 2>&1
mkdir "%DATA%" >nul 2>&1

xcopy "%SERVICE_BUILD%\*" "%INSTALL%\" /E /I /Y >nul
if errorlevel 1 goto :copy_failed
xcopy "%UI_BUILD%\ControleInternet.exe*" "%INSTALL%\" /Y >nul
if errorlevel 1 goto :copy_failed

rem Somente SYSTEM e Administradores podem alterar configuracao e backup.
rem Usuarios normais recebem leitura; as alteracoes passam pelo named pipe.
icacls "%DATA%" /inheritance:r ^
    /grant:r "*S-1-5-18:(OI)(CI)F" ^
    "*S-1-5-32-544:(OI)(CI)F" ^
    "*S-1-5-32-545:(OI)(CI)R" >nul
if errorlevel 1 (
    echo ERRO: nao foi possivel proteger a pasta de configuracao.
    exit /b 1
)

sc.exe create "%SERVICE%" ^
    binPath= "\"%INSTALL%\ControleInternetService.exe\"" ^
    start= auto ^
    obj= LocalSystem ^
    DisplayName= "Controle de Internet" >nul
if errorlevel 1 goto :service_failed

sc.exe description "%SERVICE%" "Aplica o bloqueio de sites configurado pelo Controle de Internet." >nul
sc.exe failure "%SERVICE%" reset= 0 actions= restart/5000/restart/5000/restart/5000 >nul
sc.exe start "%SERVICE%" >nul
if errorlevel 1 goto :start_failed

echo.
echo Instalacao concluida.
echo Interface: "%INSTALL%\ControleInternet.exe"
echo A interface nao exige elevacao ou UAC.
exit /b 0

:copy_failed
echo ERRO: nao foi possivel copiar os arquivos.
exit /b 1

:service_failed
echo ERRO: nao foi possivel registrar o Windows Service.
exit /b 1

:start_failed
echo ERRO: o servico nao iniciou. Restaurando qualquer alteracao de proxy.
"%INSTALL%\ControleInternetService.exe" --restore-proxy
sc.exe delete "%SERVICE%" >nul 2>&1
exit /b 1
