# Controle de Acesso à Internet

Aplicação Windows para bloquear o acesso a sites e, se desejado, liberar somente uma lista de domínios permitidos.

Desenvolvida em C# / Windows Forms / .NET Framework 4.6.2, com um Windows Service que mantém o bloqueio mesmo com a tela de configuração fechada. Compatível com Windows 7, Windows 10 e Windows 11.

## Funcionalidades

- Bloquear todos os sites, ou
- Bloquear todos os sites **exceto** os domínios (e subdomínios) de uma lista de permissões
- Proteger a tela de configuração por senha (hash, nunca texto puro)
- Persistir a configuração em arquivo local (JSON)
- Manter o bloqueio via Windows Service, independente da interface

O mecanismo aprovado e implementado é um proxy HTTP local no Windows Service, combinado somente com o proxy WinINet/Internet Options por máquina. Não altera WinHTTP, DNS ou Windows Firewall e não descriptografa HTTPS.

## Requisitos

- Windows 7 SP1, Windows 10 ou Windows 11
- .NET Framework 4.6.2
- Para compilar: Visual Studio 2017 ou posterior com o targeting pack do .NET Framework 4.6.2

## Compilação

```text
1. Abrir ControleInternet.sln
2. Compilar a configuração Release
```

Ou, no Developer Command Prompt:

```bat
msbuild ControleInternet.sln /p:Configuration=Release
```

## Instalação

1. Compile em `Release`.
2. Execute `setup\install.bat` **uma vez como administrador**.
3. Abra:

```text
C:\Program Files\ControleInternet\ControleInternet.exe
```

Na primeira abertura, defina a senha própria do ControleInternet.

`ControleInternet.exe` possui manifesto `asInvoker`: seu uso normal não pede UAC e funciona com usuário comum do Windows. A interface conversa com o serviço por um named pipe local; o serviço valida a senha, grava a configuração protegida e altera o proxy como `LocalSystem`.

## Utilização

| Bloquear todos | Liberar lista | Resultado |
|---|---|---|
| desmarcado | ignorado | Navegação normal |
| marcado | desmarcado | Todos os sites bloqueados |
| marcado | marcado | Apenas domínios cadastrados e seus subdomínios |

Cadastre somente o domínio, por exemplo `gov.br`, sem protocolo ou caminho. Ao liberar `gov.br`, `www.gov.br` e `login.gov.br` também são permitidos; `outrogov.br` não é.

Depois de clicar em **Salvar**, a interface pode ser fechada. O serviço mantém o bloqueio.

## Arquivos e segurança

- Configuração: `C:\ProgramData\ControleInternet\config.json`
- Backup temporário do proxy: `C:\ProgramData\ControleInternet\windows-backup.json`
- Log do serviço: `C:\ProgramData\ControleInternet\service.log`
- Proxy local: `127.0.0.1:18754`

A senha usa PBKDF2 com salt aleatório e 100.000 iterações. Somente `LocalSystem` e administradores do Windows acessam a pasta de dados; usuários normais consultam e alteram a configuração exclusivamente pelo serviço, mediante a senha da aplicação.

## Desinstalação

Execute `setup\uninstall.bat` como administrador. O script:

1. para o serviço;
2. restaura exatamente a configuração anterior de proxy;
3. remove o serviço e os arquivos.

Se a restauração falhar, a desinstalação é interrompida e o backup é preservado.

## Testes

Os testes cobrem hash de senha, persistência JSON, protocolo administrativo e limites corretos de subdomínio:

```bat
msbuild tests\ControleInternet.Common.Tests.csproj /p:Configuration=Release
tests\bin\Release\ControleInternet.Common.Tests.exe
```

## Limitações deliberadas

O filtro controla HTTP/HTTPS de Chrome, Edge e programas que respeitam o proxy do Windows. Programas com proxy próprio, VPNs e alterações deliberadas feitas por um administrador estão fora do escopo.

Consulte [ARCHITECTURE.md](ARCHITECTURE.md) para as decisões técnicas e o comportamento de falha.
