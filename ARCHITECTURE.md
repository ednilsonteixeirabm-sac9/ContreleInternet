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
2. se `Bloquear todos os sites` estiver desmarcado: restaurar a configuração original de proxy, se tiver sido alterada por este programa;
3. se estiver marcado: garantir o proxy local no ar e apontar o proxy WinINet/Internet Options por máquina para ele;
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

Esse backup é criado **antes** da primeira alteração do proxy do sistema. Se já existir, não é sobrescrito com valores já modificados pelo próprio programa.

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
- `LocalHttpProxy` — **não implementar nesta etapa.**

---

## 5. Mecanismo escolhido para bloquear e liberar sites

### 5.1 Decisão revisada

**Proxy HTTP local, embutido no Windows Service, combinado somente com a configuração de proxy do Windows usada por navegadores (WinINet/Internet Options), em nível de máquina.**

O proxy global do **WinHTTP foi eliminado**. Também foi eliminada a regra de firewall para UDP 443. Essas duas alterações alcançavam componentes que estão fora do escopo e não são necessárias para direcionar Chrome e Edge a um proxy HTTP explícito.

Não será usado arquivo `hosts`, DNS, proxy WinHTTP global, regra de firewall, driver de rede, inspeção de conteúdo, certificado raiz, MITM ou descriptografia TLS.

### 5.2 Configurações do Windows que serão alteradas

Antes da primeira alteração, o serviço salva os valores originais. Enquanto `Bloquear todos os sites` estiver marcado, altera somente:

| Configuração | Valor durante o bloqueio | Por que é necessária |
|---|---|---|
| `HKLM\SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Internet Settings\ProxySettingsPerUser` | `0` | Faz a configuração manual de proxy valer por máquina. Sem isso, o serviço executado como `LocalSystem` alteraria a sessão errada ou teria de editar o perfil de cada usuário. |
| `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings\ProxyEnable` | `1` | Ativa o proxy manual lido como configuração de Internet do Windows. |
| `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings\ProxyServer` | `http=127.0.0.1:18754;https=127.0.0.1:18754` | Direciona tanto HTTP quanto HTTPS ao proxy local. O prefixo `https=` identifica URLs HTTPS; o proxy usado continua sendo um proxy HTTP comum. |
| `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings\ProxyOverride` | somente loopback local | Impede que endereços locais sejam encaminhados ao proxy. Não haverá `<local>`, pois ele liberaria todos os nomes sem ponto. |
| `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings\AutoConfigURL` | ausente/vazio | Evita que um script PAC anteriormente configurado tenha precedência e mande o navegador diretamente à Internet. |
| Flags WinINet da conexão (`INTERNET_PER_CONN_FLAGS`) | somente `PROXY_TYPE_PROXY`, sem `PROXY_TYPE_AUTO_DETECT` nem `PROXY_TYPE_AUTO_PROXY_URL` | Desativa WPAD/autodetecção e impede que uma configuração automática tenha precedência sobre o proxy manual. Não haverá fallback `DIRECT`. |

O serviço notificará a mudança com `InternetSetOption(INTERNET_OPTION_SETTINGS_CHANGED)` e `InternetSetOption(INTERNET_OPTION_REFRESH)`. Quando a alteração for feita pela interface, essa notificação também será executada na sessão do usuário atual. Isso reduz a necessidade de logoff; abas já abertas podem exigir recarga.

Não serão alterados:

- proxy global WinHTTP (`netsh winhttp`);
- Windows Firewall;
- DNS dos adaptadores;
- arquivo `hosts`;
- certificados;
- configurações próprias de Chrome ou Edge;
- Windows Update ou serviços do Windows.

Observação: os valores exatos existentes — inclusive ausência de valor, PAC e bypass anteriores — serão copiados antes da mudança e restaurados depois. Não serão substituídos por “padrões” inventados.

### 5.3 WinHTTP

**Sim, WinHTTP pode e será eliminado.**

Chrome e Edge, no comportamento padrão no Windows, leem as configurações de proxy do sistema no formato WinINet/Internet Options. Eles possuem seu próprio resolvedor Chromium; não precisam que o proxy global de `netsh winhttp` seja modificado.

O proxy WinHTTP é usado principalmente por serviços e componentes do Windows. Alterá-lo ampliaria o bloqueio para Windows Update e outros processos fora do escopo, sem melhorar o controle normal de navegação no Chrome/Edge.

### 5.4 Como Chrome e Edge serão direcionados ao proxy

1. O serviço escuta apenas em loopback (`127.0.0.1`, porta proposta `18754`).
2. A configuração de proxy do Windows por máquina aponta HTTP e HTTPS para esse endereço.
3. Chrome e Edge, quando não possuem política, extensão ou linha de comando de proxy que substitua a configuração do sistema, leem essa configuração e enviam os pedidos ao serviço.
4. O serviço aplica exatamente as três regras:
   - bloqueio desmarcado: proxy original restaurado; navegação normal;
   - bloqueio marcado e allowlist desmarcada: todos os hosts recusados;
   - bloqueio marcado e allowlist marcada: somente domínio cadastrado ou host terminado em `"." + domínio` é encaminhado.

Outros navegadores que respeitam as configurações de proxy do Windows também passam pelo serviço. Programas que deliberadamente ignoram o proxy do sistema estão fora do escopo definido.

### 5.5 Tratamento de HTTP, HTTPS e domínios

Para HTTP, o navegador envia ao proxy a requisição com o hostname no cabeçalho `Host`.

Para HTTPS, o navegador envia primeiro:

```text
CONNECT exemplo.com:443
```

O serviço decide usando `exemplo.com`. Se permitido, abre uma conexão TCP ao destino e apenas transporta os bytes TLS. A negociação TLS ocorre diretamente entre navegador e site:

- nenhuma descriptografia;
- nenhum certificado instalado;
- nenhum MITM;
- nenhum acesso ao conteúdo, caminho da URL, formulário ou senha.

O domínio é obtido somente do `Host` HTTP ou do destino do `CONNECT`. A regra de subdomínio continua sendo igualdade ou sufixo `"." + domínio`, respeitando o limite DNS.

### 5.6 QUIC / HTTP/3

**Não será criada regra de firewall UDP 443.**

QUIC para um site de origem é uma conexão direta UDP. Quando Chrome/Edge têm um proxy HTTP explícito para a URL, a rota selecionada é o proxy; um proxy HTTP comum não transporta a conexão direta HTTP/3 até a origem. O navegador usa HTTP sobre o proxy ou, para HTTPS, um túnel `CONNECT` em TCP. Portanto, a configuração explícita não deve ser contornada por uma tentativa QUIC direta.

Uma regra global contra UDP 443 afetaria outros aplicativos e protocolos e contrariaria o objetivo de alteração mínima. Ela só seria reconsiderada mediante evidência reproduzível, nos testes de compatibilidade, de que uma versão suportada do Chrome/Edge ignora o proxy explícito e alcança uma origem por QUIC. Qualquer reconsideração exigirá nova aprovação; não será adicionada silenciosamente.

### 5.7 Compatibilidade

As APIs e configurações usadas existem no Windows 7, Windows 10 e Windows 11:

| Necessidade | Mecanismo |
|---|---|
| Proxy por máquina | `ProxySettingsPerUser = 0` e valores WinINet/Internet Options em HKLM |
| Notificação da mudança | `InternetSetOption` (`SETTINGS_CHANGED` e `REFRESH`) |
| Proxy local | `System.Net.Sockets.TcpListener` do .NET Framework 4.6.2 |
| Processo persistente | Windows Service (`System.ServiceProcess`) |

Chrome nas versões compatíveis com Windows 7 usa as configurações de proxy do sistema. Edge Chromium não possui versão atualmente suportada no Windows 7; no Windows 10 e 11, Chrome e Edge usam essas configurações por padrão.

### 5.8 Quando o bloqueio é desativado

O serviço:

1. restaura exatamente os valores WinINet/Internet Options copiados antes da ativação;
2. notifica o Windows e a sessão administrativa da mudança;
3. para de aceitar novas conexões no proxy local.

A lista de domínios continua salva, mas não interfere na navegação. Como WinHTTP, firewall e DNS nunca foram alterados, não existe restauração para esses componentes.

### 5.9 Se o serviço parar inesperadamente

Se o bloqueio estiver ativo, o proxy do sistema permanece apontando para `127.0.0.1:18754`, mas não haverá processo atendendo. Chrome, Edge e navegadores que usam o proxy mostrarão erro de conexão com o proxy; **não haverá liberação acidental dos sites**. É comportamento fail-closed.

O Windows Service será configurado para reiniciar automaticamente após falha. Quando voltar, relê a configuração e reabre o proxy.

Se as tentativas de recuperação falharem, a desinstalação administrativa restaura os valores diretamente do backup, sem depender do serviço em execução. Se o bloqueio já estiver desativado quando o serviço parar, a navegação permanece normal.

---

## 6. As três situações do PRD

| Checkbox 1 bloquear todos | Checkbox 2 liberar lista | Resultado |
|---|---|---|
| desmarcado | (ignorado) | Configuração original de proxy restaurada. Internet normal. A lista permanece no arquivo, sem efeito. |
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
3. só então alterar o proxy WinINet/Internet Options;
4. se a etapa 3 falhar: restaurar o backup imediatamente e registrar o erro.

A interface, ao salvar, não deixa o sistema “pela metade”: ou aplica a nova config, ou reverte ao último estado consistente.

### Desinstalação correta

O `uninstall.bat` (executado como administrador):

1. para o serviço;
2. restaura o proxy WinINet/Internet Options a partir do backup;
3. desinstala o serviço;
4. apaga `C:\ProgramData\ControleInternet`.

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
4. **Políticas ou linha de comando do navegador:** uma política empresarial, extensão autorizada ou parâmetro de inicialização que defina outro proxy pode ter precedência sobre o proxy do sistema.
5. **Edge no Windows 7:** não há Edge Chromium suportado; no 7 o alvo é o Chrome (e o Internet Explorer, que também usa WinINet, embora o PRD não o cite).
6. **Domínios internacionalizados (IDN):** o navegador envia forma punycode (`xn--...`) no `CONNECT`. O cadastro deve usar o mesmo texto que o navegador envia, ou a implementação (após aprovação) normaliza com `System.Globalization.IdnMapping`, API já existente no 4.6.2.
7. **Não bloqueia qualquer protocolo**, só o que o sistema manda ao proxy HTTP. O alvo do PRD é site HTTP/HTTPS no Chrome/Edge.
8. **Reinício do navegador:** depois de Salvar, a UI avisa o WinINet da sessão atual; mesmo assim, abas já abertas podem precisar ser recarregadas.

Nenhuma dessas limitações exige trocar o mecanismo.

---

## 10. O que não será feito

Conforme o PRD, não haverá horários, perfis, usuários, histórico, relatórios, painel web, banco de dados, categorias, bloqueio por palavra, controle de aplicativos, nuvem, SAC9, nem troca silenciosa do mecanismo após a aprovação.

---

## 11. Pedido de aprovação

Para seguir à implementação, é necessário aprovar explicitamente:

**Mecanismo: proxy HTTP local no Windows Service + proxy WinINet/Internet Options por máquina, sem WinHTTP e sem regra de firewall.**

Enquanto essa aprovação não existir, não será escrito o código de `LocalHttpProxy` nem `SystemProxy`.
