# Controle de Acesso à Internet

Aplicação Windows para bloquear o acesso a sites e, se desejado, liberar somente uma lista de domínios permitidos.

Desenvolvida em C# / Windows Forms / .NET Framework 4.6.2, com um Windows Service que mantém o bloqueio mesmo com a tela de configuração fechada. Compatível com Windows 7, Windows 10 e Windows 11.

## Estado atual

A **arquitetura está proposta e aguarda aprovação**.

Nenhum código do mecanismo de bloqueio foi escrito. O documento completo está em [ARCHITECTURE.md](ARCHITECTURE.md).

## O que o produto fará (após a implementação)

- Bloquear todos os sites, ou
- Bloquear todos os sites **exceto** os domínios (e subdomínios) de uma lista de permissões
- Proteger a tela de configuração por senha (hash, nunca texto puro)
- Persistir a configuração em arquivo local (JSON)
- Manter o bloqueio via Windows Service, independente da interface

## Como executar (depois da implementação)

Requisitos: Windows 7 SP1 / 10 / 11 com .NET Framework 4.6.2, Visual Studio capaz de abrir a solution, execução da instalação **como administrador**.

```text
1. Abrir ControleInternet.sln
2. Compilar a configuração Release
3. Executar setup\install.bat como administrador
4. Abrir ControleInternet.exe, definir/informar a senha e configurar
```

Instruções detalhadas de instalação, desinstalação e restauração das configurações do Windows serão incluídas quando o código for implementado.
