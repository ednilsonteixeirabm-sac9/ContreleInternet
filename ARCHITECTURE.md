# Arquitetura — Controle de Acesso à Internet

**Status:** proposta para aprovação. Nenhum código do mecanismo de bloqueio foi implementado.

Este documento cobre exclusivamente a primeira etapa pedida no PRD:

1. Arquitetura
2. Estrutura da solution
3. Projetos
4. Principais arquivos e classes
5. Mecanismo de bloqueio/liberação de domínios
6. Compatibilidade com Windows 7, 10 e 11
7. Limitações técnicas

Nada do mecanismo de bloqueio será escrito até esta arquitetura ser aprovada. Depois de aprovada, o mecanismo **não** será trocado por DNS, firewall como filtro principal, proxy externo ou outra abordagem sem autorização.

---

## 1. Arquitetura geral

O produto tem dois processos distintos, com um terceiro projeto apenas para código compartilhado:

| Componente | Tipo | Papel |
|---|---|---|
| `ControleInternet.exe` | Windows Forms | Tela de senha e tela de configuração. Não aplica o bloqueio de forma contínua. |
| `ControleInternetService` | Windows Service | Inicia com o Windows, lê a configuração e **é o único componente que aplica e mantém o bloqueio**. |
| `ControleInternet.Common` | Class Library | Configuração, senha, validação de domínio e regras da allowlist. Evita duplicar lógica entre os dois executáveis. |

```text
Administrador
     │
     ▼
ControleInternet.exe
  - pede senha
  - altera checkboxes e lista
  - grava C:\ProgramData\ControleInternet\config.json
     │
     │  FileSystemWatcher / releitura
     ▼
ControleInternetService  (LocalSystem, StartType = Automatic)
  - sobe um proxy HTTP local em 127.0.0.1 (e ::1)
  - configura o proxy do Windows para apontar para esse endereço
  - decide permitir ou recusar cada site pelo hostname
     │
     ▼
Chrome / Edge / demais programas que respeitam o proxy do sistema
```

Fechar a interface **não** desfaz o bloqueio. O serviço continua em execução.

### Por que um projeto compartilhado

O PRD pede dois projetos principais. `ControleInternet.Common` não é uma aplicação; é só um assembly para:

- ler/gravar o mesmo arquivo de configuração;
- validar a senha com o mesmo hash;
- aplicar a mesma regra de subdomínio na interface (validação) e no serviço (decisão de acesso).

Sem esse assembly, a mesma lógica teria de ser copiada nos dois executáveis.

---

## 2. Estrutura da solution

```text
ControleInternet.sln
│
├── ControleInternet.Common\          (Class Library, .NET Framework 4.6.2)
│   ├── AppConfig.cs
│   ├── ConfigStore.cs
│   ├── PasswordHasher.cs
│   ├── DomainName.cs
│   └── Paths.cs
│
├── ControleInternet\                 (WinExe, Windows Forms, .NET Framework 4.6.2)
│   ├── Program.cs
│   ├── LoginForm.cs
│   ├── MainForm.cs
│   ├── SiteEditForm.cs
│   └── Native\WinInetRefresh.cs      (notificar o Windows que o proxy mudou)
│
├── ControleInternetService\          (WinExe, Windows Service, .NET Framework 4.6.2)
│   ├── Program.cs
│   ├── ControleInternetService.cs
│   ├── ConfigWatcher.cs
│   ├── SystemProxy.cs                (backup / aplicar / restaurar proxy)
│   ├── QuicFirewall.cs               (regra UDP 443 complementar)
│   └── LocalHttpProxy.cs             (mecanismo de bloqueio — não implementar agora)
│
├── setup\
│   ├── install.bat
│   └── uninstall.bat
│
├── ARCHITECTURE.md
└── README.md
```

Plataforma-alvo de todos os projetos: **.NET Framework 4.6.2**, `AnyCPU` preferencialmente (o Framework 4.6.2 de 32 e 64 bits já está no Windows 7 SP1 com atualização, Windows 10 e Windows 11).

Não será usado .NET Core, .NET 5+, pacotes NuGet externos nem APIs exclusivas de Windows 8/10/11.

---

## 3. Projetos

### 3.1 ControleInternet (interface)

Responsabilidades, conforme o PRD:

- pedir senha ao abrir;
- na primeira execução, se ainda não existir hash gravado, pedir a criação da senha (duas vezes) e persistir só o hash;
- exibir os dois checkboxes e a lista de domínios;
- adicionar, editar e remover sites;
- gravar `config.json`.

A interface **não** precisa ficar aberta. Depois de **Salvar**, ela pode ser fechada.

Ela também chama `InternetSetOption` no **sessão do usuário** para o Chrome/Edge enxergarem a nova configuração de proxy sem exigir logoff. A aplicação dessas configurações de Windows continua sendo responsabilidade do serviço; a interface só dispara o refresh da sessão atual.

### 3.2 ControleInternetService (serviço)

- `StartType = Automatic`
- conta: `LocalSystem`
- recuperação do Windows: reiniciar o serviço após falha (primeira, segunda e subsequentes)

Na inicialização:

1. ler `config.json`;
2. se `Bloquear todos os sites` estiver desmarcado: restaurar proxy e regra de firewall, se tiverem sido alterados por este programa;
3. se estiver marcado: garantir o proxy local no ar, apontar o proxy do sistema para ele e, se necessário, aplicar a regra complementar de UDP 443;
4. vigiar o arquivo de configuração e reaplicar quando a interface salvar.

### 3.3 ControleInternet.Common

Código sem UI e sem dependência de `System.ServiceProcess`.

---

## 4. Principais arquivos e classes

### Configuração (`AppConfig` + `ConfigStore`)

Arquivo:

`C:\ProgramData\ControleInternet\config.json`

Formato previsto (JSON via `DataContractJsonSerializer`, presente no .NET 4.6.2, sem biblioteca extra):

```json
{
  "blockAllSites": true,
  "allowListedSites": true,
  "allowedDomains": [
    "sac9.com.br",
    "gov.br",
    "sefaz.rj.gov.br",
    "web.whatsapp.com"
  ],
  "passwordSalt": "<base64>",
  "passwordHash": "<base64>",
  "passwordIterations": 100000
}
```

A senha **nunca** é gravada em texto puro.

Backup das configurações originais do Windows (para desinstalação e para o modo “acesso normal”):

`C:\ProgramData\ControleInternet\windows-backup.json`

Esse backup é criado **antes** da primeira alteração de proxy/firewall. Se já existir, não é sobrescrito com valores já modificados pelo próprio programa.

### Senha (`PasswordHasher`)

- algoritmo: **PBKDF2** com `Rfc2898DeriveBytes` (HMAC-SHA1), API nativa do .NET Framework 4.6.2;
- salt aleatório de 16 bytes;
- 100.000 iterações;
- comparação em tempo constante.

SHA-1 dentro de PBKDF2 com salt e muitas iterações é o mecanismo seguro disponível sem código extra nem APIs de .NET mais novo. Construtores PBKDF2 com SHA-256 (`HashAlgorithmName`) **não** existem no 4.6.2.

### Domínio (`DomainName`)

- o usuário informa só o host, sem protocolo (`gov.br`, não `https://gov.br`);
- se colar `https://gov.br/caminho`, a UI rejeita ou extrai o host — a regra de cadastro continua sendo domínio puro;
- normalização: minúsculas, remoção de ponto final, rejeição de espaços e de caracteres inválidos.

Regra de pertencimento (allowlist), a ser usada depois da aprovação:

```text
permitido(host, dominio) =
    host == dominio
    OU host termina com ("." + dominio)
```

Isso respeita o limite do rótulo DNS:

| Cadastrado | Host acessado | Resultado |
|---|---|---|
| `gov.br` | `gov.br` | permitido |
| `gov.br` | `www.gov.br` | permitido |
| `gov.br` | `login.gov.br` | permitido |
| `gov.br` | `outrogov.br` | bloqueado (`outrogov.br` não termina com `.gov.br`) |
| `gov.br` | `gov.br.outrosite.com` | bloqueado |
| `gov.br` | `meugov.br` | bloqueado |

### Formulários

- `LoginForm` — senha; na primeira execução, definição da senha.
- `MainForm` — os dois checkboxes, `ListBox` dos domínios, Adicionar / Editar / Remover, Salvar.
- `SiteEditForm` — um campo de domínio, usado tanto para incluir quanto para editar.

Checkbox 2 (`Liberar os sites listados a seguir`) só tem efeito quando o checkbox 1 está marcado. Na UI, se o bloqueio geral estiver desmarcado, o segundo checkbox e os botões da lista ficam desabilitados para deixar a regra visível, mas os dados da lista **não são apagados**.

### Serviço

- `ControleInternetService` — `OnStart` / `OnStop` / `OnShutdown`.
- `ConfigWatcher` — `FileSystemWatcher` no `config.json` + releitura defensiva.
- `SystemProxy` — backup, aplicação e restauração do proxy por máquina.
- `QuicFirewall` — cria/remove uma regra nomeada do Windows Firewall.
- `LocalHttpProxy` — **não implementar nesta etapa.**

---

## 5. Mecanismo escolhido para bloquear e liberar sites

### Decisão

**Proxy HTTP local, embutido no Windows Service, combinado com o proxy do sistema (WinINet / WinHTTP) em nível de máquina.**

Complemento (não é o mecanismo principal): uma regra do Windows Firewall bloqueando **UDP 443 de saída**, somente enquanto o bloqueio estiver ativo, para reduzir bypass por QUIC/HTTP/3.

Não será usado:

- arquivo `hosts` (não consegue “bloquear todos os sites”);
- DNS local como mecanismo principal (DoH do Chrome/Edge no Windows 10/11 ignora o DNS do sistema);
- firewall por endereço IP (CDNs mudam IP; o PRD pede domínio);
- inspeção de conteúdo HTTP/HTTPS;
- certificado raiz, MITM ou descriptografia TLS.

### 5.1 Como os sites serão bloqueados

1. O serviço abre um socket TCP em `127.0.0.1` e `::1`, porta fixa da aplicação (proposta: **18754**). Nada além do loopback.
2. O serviço grava o proxy do Windows para `127.0.0.1:18754` (e equivalente IPv6 quando aplicável), de forma **por máquina**, não só no usuário SYSTEM.
3. O navegador, ao abrir um site:
   - **HTTP:** envia o pedido ao proxy, com cabeçalho `Host`.
   - **HTTPS:** envia `CONNECT host:443`. O nome do site vai em texto claro nesse comando; o TLS continua ponta a ponta entre o navegador e o servidor. O proxy **não** vê o conteúdo da página e **não** instala certificado.
4. O serviço decide:
   - **Situação 1** — bloqueio desligado: o serviço **não** força proxy (restaura o backup). Navegação normal.
   - **Situação 2** — bloquear todos, allowlist desligada: qualquer `Host` / `CONNECT` é recusado (conexão fechada ou resposta `403`).
   - **Situação 3** — bloquear todos, allowlist ligada: só passa o que casar com a lista (e subdomínios).
5. Se o destino for permitido, o serviço abre TCP até o destino e encaminha bytes **sem interpretar o TLS**.

Endereços IP literais (`http://1.2.3.4`) não são domínio cadastrado → ficam bloqueados nas situações 2 e 3.

`localhost` / `127.0.0.1` / `::1` entram em `ProxyOverride` para o próprio proxy não entrar em loop. Isso não libera sites da Internet.

### 5.2 Como os domínios da allowlist serão identificados

Exclusivamente pelo **hostname pedido pelo navegador**:

- HTTP: campo `Host` (sem porta);
- HTTPS: alvo do `CONNECT` (sem porta).

Não há consulta a lista de IPs, não há inspeção de URL além do host, não há classificação de conteúdo.

Comparação sempre em minúsculas, depois de normalizar o host.

### 5.3 Subdomínios

Como na tabela da seção 4: igualdade ou sufixo `"." + dominio`. Não é “contains” e não é `EndsWith(dominio)` sem o ponto, precisamente para rejeitar `outrogov.br` e `meugov.br`.

### 5.4 Windows 7

APIs usadas, todas presentes no Windows 7 (Vista+ / Win7):

| Necessidade | API |
|---|---|
| Proxy por máquina | `HKLM\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Internet Settings\ProxySettingsPerUser = 0` |
| Valores de proxy | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings` (`ProxyEnable`, `ProxyServer`, `ProxyOverride`, `AutoConfigURL` limpo) |
| WinHTTP (alguns componentes do SO) | `WinHttpSetDefaultProxyConfiguration` / `netsh winhttp set proxy` |
| Aviso ao WinINet | `InternetSetOption` (`INTERNET_OPTION_SETTINGS_CHANGED` / `REFRESH`) |
| Firewall UDP 443 | COM `INetFwPolicy2` / `netsh advfirewall` (existe no Windows 7) |
| Serviço | `System.ServiceProcess` + `sc.exe` / `InstallUtil` |
| Socket do proxy | `System.Net.Sockets.TcpListener` (.NET 4.6.2) |

Não há WFP callout driver, WinDivert, TDI nem APIs de Windows 8+.

Chrome nas versões que ainda rodavam em Windows 7 respeita o proxy do sistema WinINet. Edge Chromium **não** é suportado oficialmente no Windows 7; o PRD pede Edge “nas versões compatíveis com cada sistema”, portanto Edge entra no escopo no Windows 10 e 11.

### 5.5 Windows 10 e Windows 11

O mesmo mecanismo: WinINet/WinHTTP + proxy local.

Chrome e Edge atuais no Windows usam as configurações de proxy do sistema. O `CONNECT` continua expondo o hostname sem descriptografar HTTPS.

A regra UDP 443 existe porque, nestes sistemas, Chrome/Edge podem tentar HTTP/3 (QUIC) e, em alguns casos, sair direto na UDP 443, furando o proxy. Com UDP 443 bloqueada pelo firewall do Windows, o navegador cai para TCP 443 via proxy.

Não será usada a API de AppContainer, `Windows.Networking`, filtro de família Microsoft Family Safety, nem Defender.

### 5.6 Dependências externas

Nenhuma.

- sem servidor na Internet;
- sem lista de categorias online;
- sem pacote NuGet;
- sem driver de terceiros;
- sem certificado.

Única dependência: .NET Framework 4.6.2 já instalado no Windows.

### 5.7 Configurações do Windows que serão alteradas

Somente enquanto `Bloquear todos os sites` estiver **marcado**, e sempre com backup prévio:

1. **Proxy por máquina (WinINet)**  
   - `ProxySettingsPerUser = 0`  
   - `ProxyEnable = 1`  
   - `ProxyServer = 127.0.0.1:18754`  
   - `ProxyOverride = localhost;127.0.0.1;::1`  
   - `AutoConfigURL` vazio (para o PAC antigo não mandar o tráfego para outro lugar)

2. **Proxy WinHTTP**  
   - alinhado ao mesmo servidor, para componentes que não usam WinINet.

3. **Firewall do Windows**  
   - uma regra nomeada, por exemplo `ControleInternet_BlockQUIC`, outbound UDP destino 443, ação bloquear.  
   - nenhuma outra regra de firewall.

Quando o bloqueio for **desmarcado**, na desinstalação correta, ou se a aplicação da nova config falhar depois do backup: restaurar exatamente os valores salvos em `windows-backup.json` e apagar a regra de firewall deste programa.

Nada de alterar DNS dos adaptadores, arquivo `hosts`, GPOs extras, certificados ou serviços de rede do Windows.

---

## 6. As três situações do PRD

| Checkbox 1 bloquear todos | Checkbox 2 liberar lista | Resultado |
|---|---|---|
| desmarcado | (ignorado) | Proxy e regra QUIC restaurados. Internet normal. A lista permanece no arquivo, sem efeito. |
| marcado | desmarcado | Proxy local ativo. Todo `Host`/`CONNECT` recusado. |
| marcado | marcado | Proxy local ativo. Só passam os domínios cadastrados e seus subdomínios. |

---

## 7. Comportamento em falha (PRD §12)

### Serviço parou de forma inesperada (crash)

- O Windows está configurado para **reiniciar o serviço automaticamente**.
- Enquanto o processo não volta: o proxy do sistema continua apontando para `127.0.0.1:18754` e **não há processo escutando**. Navegadores que usam o proxy **não acessam sites**. É falha **fechada** (fail-closed), não “liberou a Internet”.
- Isso atende “o bloqueio não depende da interface” e evita uma janela em que o crash do serviço libere tudo.
- Não deixa o PC **permanentemente** sem Internet: o serviço sobe de novo sozinho; o administrador pode abrir a UI, desmarcar o bloqueio e salvar; a desinstalação restaura o proxy.

### Erro no meio da instalação ou da configuração

Ordem obrigatória:

1. criar pasta em ProgramData;
2. **gravar backup** se ainda não existir;
3. só então alterar proxy/firewall;
4. se a etapa 3 falhar: restaurar o backup imediatamente e registrar o erro.

A interface, ao salvar, não deixa o sistema “pela metade”: ou aplica a nova config, ou reverte ao último estado consistente.

### Desinstalação correta

O `uninstall.bat` (executado como administrador):

1. para o serviço;
2. restaura proxy WinINet e WinHTTP a partir do backup;
3. remove a regra `ControleInternet_BlockQUIC`;
4. desinstala o serviço;
5. apaga `C:\ProgramData\ControleInternet`.

Se o backup não existir (nunca chegou a alterar o Windows), não mexe no proxy.

---

## 8. Instalação

Sem instalador gráfico complexo na primeira versão de código (depois da aprovação):

- `setup\install.bat` — copia arquivos, `sc create` / `InstallUtil`, `sc config start= auto`, `sc failure` para restart, `net start`.
- `setup\uninstall.bat` — procedimento da seção 7.

Requer **administrador** uma vez, para registrar o serviço e poder gravar HKLM. O uso diário da UI também precisará de direitos suficientes para gravar em ProgramData e sinalizar o serviço — na prática, o administrador da máquina.

---

## 9. Limitações técnicas (aceitáveis pelo PRD)

O PRD não exige resistência a quem tem direitos de administrador e quer furar o sistema de propósito. Ainda assim, as limitações reais são:

1. **Programas que ignoram o proxy do Windows** (Firefox com configuração própria, alguns clientes HTTP, `curl` sem `-x`, aplicativos com IP fixo) não passam pelo filtro.
2. **Usuário administrador** pode parar o serviço, desfazer o proxy ou desinstalar.
3. **VPN / proxy manual** definido depois pelo usuário pode desviar o tráfego.
4. **HTTP/3 (QUIC):** mitigado pela regra UDP 443, não pelo proxy. Se essa regra for apagada à mão, o navegador moderno pode furar o proxy em alguns sites.
5. **Windows Update e outros usos WinHTTP:** com o bloqueio ligado e WinHTTP apontando para o proxy local, atualizações e alguns componentes da Microsoft podem falhar, a menos que o domínio esteja na allowlist. Não será criada exceção oculta — está fora do escopo e seria funcionalidade extra.
6. **Edge no Windows 7:** não há Edge Chromium suportado; no 7 o alvo é o Chrome (e o Internet Explorer, que também usa WinINet, embora o PRD não o cite).
7. **Domínios internacionalizados (IDN):** o navegador envia forma punycode (`xn--...`) no `CONNECT`. O cadastro deve usar o mesmo texto que o navegador envia, ou a implementação (após aprovação) normaliza com `System.Globalization.IdnMapping`, API já existente no 4.6.2.
8. **Não bloqueia qualquer protocolo**, só o que o sistema manda ao proxy HTTP. O alvo do PRD é site HTTP/HTTPS no Chrome/Edge.
9. **Reinício do navegador:** depois de Salvar, a UI avisa o WinINet da sessão atual; mesmo assim, abas já abertas podem precisar ser recarregadas.

Nenhuma dessas limitações exige trocar o mecanismo.

---

## 10. O que não será feito

Conforme o PRD, não haverá horários, perfis, usuários, histórico, relatórios, painel web, banco de dados, categorias, bloqueio por palavra, controle de aplicativos, nuvem, SAC9, nem troca silenciosa do mecanismo após a aprovação.

---

## 11. Pedido de aprovação

Para seguir à implementação, é necessário aprovar explicitamente:

**Mecanismo: proxy HTTP local no Windows Service + proxy do sistema por máquina (WinINet/WinHTTP), com regra complementar de firewall UDP 443 enquanto o bloqueio estiver ativo.**

Enquanto essa aprovação não existir, não será escrito o código de `LocalHttpProxy`, `SystemProxy` nem `QuicFirewall`.
