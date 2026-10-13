# ControleSala

Substituto do Veyon para a sala de informática: o professor vê e controla os PCs dos alunos,
com transmissão de tela em vídeo H.264 (QuickSync/GPU) em vez de VNC.

> **Estado atual: prova de conceito (v0.1).** Transmissão de tela (grade + tela cheia), perfis de professor,
> sala com chave secreta, conexão **autenticada e criptografada**, e instalador. **Ainda faltam** mouse/teclado
> remoto, transferência de arquivos, comandos e limpeza (ver `ROADMAP.md`). Sem chave da sala o agente só
> aceita conexões do próprio PC.

## Atualização (detecta e avisa)

O console do professor **checa o GitHub** (repositório `degodelf/controle-sala`) ao abrir. Se houver uma versão
mais nova, aparece uma **faixa de aviso** no topo. Clicando em **Atualizar**, o professor vê as novidades e escolhe:
**Baixar** (o console baixa o instalador da Release e confere a assinatura do autor) e então **Atualizar este
computador** (abre o instalador, que instala por cima mantendo tudo) e/ou **Enviar aos alunos** (o fluxo da rede).
Nada acontece sem o clique; os alunos continuam sendo atualizados pelo professor, não pela internet.

Para isso funcionar, a Release do GitHub precisa ter o **instalador assinado**. Depois que o GitHub Actions gerar o
Setup (não assinado), assine-o e envie o assinado para a Release:
`ControleSala.Assinador assinar ControleSala-Setup-<versão>.exe <versão>`.

## Gerar nova versão (GitHub Actions)

O instalador é gerado automaticamente a cada versão. Para lançar uma versão nova:

1. Atualize `<Version>` no `Directory.Build.props` (ex.: `0.3.1`) — a tag abaixo deve casar com ela.
2. Faça commit e crie a tag: `git tag v0.3.1 && git push --tags`.
3. O GitHub Actions (`.github/workflows/build-release.yml`) compila num runner Windows: instala o .NET 10,
   baixa o FFmpeg (`terceiros/baixar-ffmpeg.ps1`), instala o Inno Setup, roda `instalador/gerar-instalador.ps1`
   e cria a **Release** com o `ControleSala-Setup-<versão>.exe`.
4. O instalador sai **sem assinatura** (a chave do autor não fica no GitHub). Para usar na **atualização pela
   rede**, baixe o Setup da Release e assine no seu PC:
   `ControleSala.Assinador assinar ControleSala-Setup-<versão>.exe <versão>`.
   Para instalar num PC novo por pendrive, o Setup não assinado já serve.

As DLLs do FFmpeg (~107 MB) não ficam no git; o `terceiros/baixar-ffmpeg.ps1` as baixa (localmente e no CI).

## Instalar (instalador)

`instalador\gerar-instalador.ps1` publica os dois programas e gera `instalador\saida\ControleSala-Setup-<versão>.exe`
(a versão vem do `<Version>` do `Directory.Build.props` — mude só lá; precisa do .NET 10 SDK e do Inno Setup 6). O assistente tem: **Bem-vindo → Tipo (Professor ou Aluno) →
[Aluno: arquivo da sala + senha] → Pasta de instalação → Instalar → Concluir**, e aparece em
"Adicionar ou remover programas".

- **Professor:** instala o programa de controle, atalhos e a liberação do firewall para a busca na rede.
- **Aluno:** instala o agente como **serviço do Windows** (liga com o PC, reinicia se cair, roda como SYSTEM —
  o aluno não consegue encerrar nem abrir), grava a chave da sala numa pasta só de administrador, libera o
  firewall **só para a rede local** e coloca o ícone informativo perto do relógio. Não cria atalho nenhum.
- Silencioso: `ControleSala-Setup-0.2.0.exe /VERYSILENT /TIPO=aluno /SALA="E:\Lab1.controlesala" /SENHA=...`
  (sem `/SALA` procura um `.controlesala` ao lado do Setup; sem `/SENHA` usa a variável `CONTROLESALA_SENHA`).
- Teste sem administrador: `/CURRENTUSER` instala os arquivos mas **não** cria serviço nem regras de firewall.
- Instalação silenciosa com pacote/senha errados não abre janela: encerra com código de saída diferente de 0
  (veja o motivo com `/LOG=arquivo.log`).
- Desinstalar (PC de aluno) também libera internet/sites que estavam bloqueados e remove a limpeza agendada
  (o desinstalador chama `ControleSala.Agente.exe --desfazer`).
- Sempre gere o instalador com `instalador\gerar-instalador.ps1`: ele publica em pasta limpa (publicar por cima
  de uma pasta antiga faz o SDK apagar as DLLs do WPF e do FFmpeg).

## Atualizar

Rode o Setup da versão nova **por cima** da instalada (mesmo `AppId`): os arquivos são trocados e o resto fica.

- O instalador **lembra o tipo** do computador (professor/aluno) e não pergunta de novo. A 0.1.0 não guardava o
  tipo: nesse caso ele descobre pelo programa que está na pasta instalada.
- **PC de aluno:** se já tem sala gravada, oferece **"Manter a sala atual"** (padrão) e não pede pacote nem senha;
  escolha "Trocar de sala" só se o PC mudou de sala. O serviço é parado antes e religado depois.
- **PC do professor:** sala, chave, professores, papel de parede, limpeza etc. ficam em `%AppData%\ControleSala` e não
  são tocados. Se o programa estiver aberto, o instalador pede para fechar.
- Silencioso (vários PCs): `ControleSala-Setup-0.3.0.exe /VERYSILENT` — sem `/TIPO`, `/SALA` nem `/SENHA`.
  Com `/SALA=... /SENHA=...` troca a sala junto.
- Testado com administrador (2026-10-12): 0.1.0 aluno → 0.2.0 silencioso, serviço parado e religado, sala mantida,
  desinstalação limpa (inclusive a regra de bloqueio de internet).

### Atualizar os alunos pela rede (a partir da 0.3.0)

No uso real: o autor gera o Setup novo (assinado) → o professor instala por cima no PC dele → no programa,
**Computadores → Atualizar o ControleSala dos alunos...** (ou o aviso "⚠ N computadores com versão antiga" na barra de
status) → marca os desatualizados → **Atualizar**. Cada PC recebe o instalador pela conexão da sala, confere e se
atualiza sozinho em ~1 minuto (some da grade e volta). Ninguém passa de PC em PC.

- **Onde está o instalador:** o Setup do professor guarda uma cópia de si mesmo em
  `C:\Program Files\ControleSala\atualizacao\ControleSala-Setup.exe`. A janela também deixa escolher outro arquivo.
- **Segurança — o PC do aluno só instala se:** (1) chegou pelo canal autenticado da sala; (2) o tamanho e o SHA-256
  conferem; (3) tem a **assinatura do autor** (ECDSA P-256 anexada no fim do Setup; a chave pública está em
  `Comum/AssinaturaInstalador.cs`, a privada só no PC do autor); (4) a versão é **mais nova** que a instalada (não
  volta versão). Quem tem a chave da sala não consegue mandar um programa qualquer.
- O agente informa a versão ao conectar (mensagem `VersaoAgente`). Agentes até a 0.2.0 não sabem se atualizar pela
  rede: esses, só com o Setup no próprio PC.
- PC desligado na hora: atualize depois, quando estiver ligado (a janela mostra a versão de cada um).
- **Testado com administrador (2026-10-12):** aluno 0.2.99 como serviço → professor mandou a 0.3.0 pela rede → o agente
  conferiu, instalou e voltou sozinho na 0.3.0 (serviço religado, sala mantida); desinstalação limpa (serviço, regras,
  pastas, limpeza agendada). Roteiro: `instalador\teste-atualizacao-rede-admin.ps1`.
- Ao parar, o serviço encerra **só o agente**, não o que ele abriu: o instalador (que é quem para o serviço) e os
  programas que o professor abriu para o aluno continuam.
- Linha de comando (testes): `ControleSala.Professor.exe --atualizar <Setup.exe> --para <ip:porta> [--resultado <arq>] [--esperar-versao <s>]`.

### Chave de assinatura (só o autor)

`src\ControleSala.Assinador` (não vai no instalador). A chave fica em `%AppData%\ControleSala-Assinatura`, protegida
pelo Windows (só o usuário do autor neste PC abre). O `gerar-instalador.ps1` assina sozinho; sem a chave ele avisa e
gera sem assinatura (os alunos recusam esse Setup pela rede).

- `ControleSala.Assinador exportar E:\chave-controlesala.backup` — **faça já** e guarde fora do PC (pede uma senha).
- `ControleSala.Assinador importar <arquivo>` — num PC novo. `verificar <Setup.exe>` — confere um instalador.
- Se perder a chave: os PCs já instalados só se atualizam pelo Setup no próprio PC (pendrive), com uma chave nova.

## Segurança da sala

1. No Professor, **Sala...** cria a sala (escola + nome) e uma **chave secreta** de 256 bits, guardada protegida
   pelo Windows (DPAPI) neste PC.
2. **Gerar pacote para os alunos...** grava um `.controlesala` protegido por uma **senha** escolhida na hora
   (PBKDF2-SHA256 600 mil voltas + AES-256-GCM). Leve no pendrive e apague depois.
3. O instalador (opção Aluno) pede o pacote e a senha, confere e grava a chave em `%ProgramData%\ControleSala`
   (só administrador e SYSTEM leem).
4. Toda conexão faz um **aperto de mão** com a chave (HMAC-SHA256, nos dois sentidos: o professor também confere
   que o agente conhece a chave); depois o canal inteiro vai em **AES-256-GCM** (chaves derivadas com HKDF, uma
   por sentido, contador como nonce: registro alterado, repetido ou fora de ordem é recusado).
5. Sem a chave: o agente só aceita conexões **do próprio PC** (`--rede` é recusado). Quem conecta e não prova
   a chave é cortado; quem fica parado é cortado em 5 s.
6. O professor se identifica ao conectar e o aluno vê o nome dele no cartão do ícone da bandeja.

Comandos úteis: `ControleSala.Professor.exe --gerar-pacote <saida.controlesala> --senha <s> [--escola <e> --sala <s>]`,
`ControleSala.Agente.exe --verificar-sala <arquivo> --senha <s>`, `--importar-sala`, `--rede --sala <arq> --senha <s>`.

## Estrutura

| Projeto | O que é |
|---|---|
| `src/ControleSala.Agente` | Roda no PC do aluno. Captura a tela (DXGI Desktop Duplication), converte BGRA→NV12 na GPU, codifica em H.264 e envia por TCP. |
| `src/ControleSala.Professor` | Roda no PC do professor (WPF). Conecta no agente, decodifica e exibe. |
| `src/ControleSala.Comum` | Protocolo, codificador e decodificador H.264 (FFmpeg). |
| `terceiros/ffmpeg` | DLLs do FFmpeg 8.1 (build LGPL), carregadas via FFmpeg.AutoGen. |

Ordem dos encoders: `h264_qsv` (Intel QuickSync) → `h264_mf` (Media Foundation, qualquer GPU) → `libopenh264` (software).

## Compilar

Requer o .NET 10 SDK.

```bash
dotnet build ControleSala.slnx
```

Versão autossuficiente (não precisa do .NET instalado no PC de destino):

```bash
dotnet publish src/ControleSala.Agente -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=none -o publicar/Agente
dotnet publish src/ControleSala.Professor -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=none -o publicar/Professor
```

## Testar

No mesmo PC: abra `ControleSala.Agente.exe`, depois `ControleSala.Professor.exe` e clique em **Conectar**.

Entre dois PCs (sem instalar): no Professor crie a sala e gere o pacote; no PC do aluno rode
`ControleSala.Agente.exe --rede --sala <pacote> --senha <senha>` (o Windows vai pedir para liberar no firewall);
no Professor, **Computadores... → Procurar na rede...**. Feche o agente (Ctrl+C) ao terminar.

Opções do agente: `--fps 30`, `--kbps 6000`, `--porta 47810`, `--encoder auto|h264_qsv|h264_mf|libopenh264`.

`teste/cliente_teste.py` é um cliente sem interface que mede FPS/banda/atraso e grava o H.264 recebido.

## Comandos

No Professor, **Comandos → Abrir link nos computadores** (ou o botão **Abrir link** na barra): digite o endereço,
marque os computadores e clique em Enviar. O link abre no navegador padrão de cada aluno, **na conta dele** (o agente
roda como serviço/SYSTEM, mas abre o navegador com o usuário da sessão). Só passam links **http/https** —
file:, javascript:, programas etc. são recusados nas duas pontas. Vai pelo canal autenticado da sala.

**Mensagem na tela:** Comandos → Enviar mensagem. Aparece um aviso no canto da tela do aluno (com o nome do professor),
que some em ~15 s ou quando o aluno clica em OK.

**Bloquear / desbloquear a tela:** Comandos → Bloquear a tela. A tela do aluno fica coberta com um recado
("Atenção ao professor" por padrão) e o Alt+F4 não fecha; só o professor desbloqueia (Comandos → Desbloquear a tela).

Sem janela (teste): `ControleSala.Professor.exe --comando AbrirLink|Mensagem|BloquearTela|DesbloquearTela "<texto>" --para <host:porta>`.

**Abrir / fechar programa:** Comandos → Programas permitidos monta a lista (nome amigável + o .exe; ex.: Calculadora →
calc.exe). Depois, Comandos → Abrir programa / Fechar programa: escolha o nome na lista e os computadores. O programa
abre na conta do aluno. Interpretadores (cmd, powershell, wscript...) são recusados nas duas pontas. O "fechar" pede
para o programa sair com jeito (deixa salvar) e, se não sair em 2 s, encerra. Lista em `%AppData%\ControleSala\programas.json`.

**Desligar / reiniciar / encerrar sessão (logoff):** Comandos → Desligar/Reiniciar/Logoff. O professor **confirma antes**
(Sim/Não, dizendo quantos computadores) e cada aluno vê um **aviso de 20 segundos** antes de acontecer (o Windows mostra
a contagem). Precisa do agente instalado como serviço.

**Silenciar / reativar o som:** Comandos → Silenciar o som / Reativar o som. Liga/desliga o mudo do PC do aluno.

**Ligar os computadores (Wake-on-LAN):** Comandos → Ligar os computadores. Liga pela rede os PCs que já ficaram online
alguma vez (o ControleSala guarda o endereço de rede — MAC — de cada um) e com o Wake-on-LAN ativado na BIOS.
Funciona mesmo com o PC desligado (é enviado pelo professor, não pelo agente).

**Bloquear a internet / sites:** Comandos tem três opções. **Bloquear TODA a internet** corta o acesso à internet
(a rede local e o ControleSala continuam funcionando) via regra de firewall. **Bloquear sites da lista** bloqueia só
os sites de **Sites bloqueados...** (ex.: redes sociais), via arquivo hosts, deixando o resto funcionar.
**Liberar a internet** desfaz os dois. Precisa do agente como serviço.

**Limpeza automatizada:** Comandos → Limpeza automática (configurar). O professor escolhe o que apagar na conta do
aluno (Downloads, Área de Trabalho, Documentos, histórico/cache/cookies dos navegadores, Lixeira, %TEMP%), exceções
(pastas/arquivos a preservar), uma **imagem** mostrada durante a limpeza, se **restaura o papel de parede** (fundo+logo)
e a **agenda** (dias da semana + horário). Também pode **atualizar o Windows Defender** e **fazer uma verificação**
(rápida ou completa) como parte da limpeza — o Defender roda em segundo plano, sem travar o aluno. A configuração e as
imagens são enviadas aos computadores quando ficam online; o agente cria uma **tarefa agendada** no Windows (roda como
serviço, mesmo sem o professor conectado). Também há **Limpar agora** (com confirmação). **A limpeza nunca roda no PC
do professor** (ele só tem o console, não o agente) e nunca mexe na conta de administrador.

## Aparência (temas claro e escuro)

O Professor tem **barra de menus**, **barra de ferramentas com ícones** e **barra de status** (quantos PCs online,
a sala, o professor em uso e o crédito). Há **dois temas, claro e escuro**: troque pelo ícone de lua na barra de
ferramentas ou em **Exibir → Tema claro ou escuro**. A escolha fica guardada em `%AppData%\ControleSala\tema.txt`.
As cores ficam em `Cores.xaml`/`Tema.cs`, os estilos em `Estilos.xaml`/`EstilosMenu.xaml` e os ícones (vetor) em
`Icones.xaml`; todas as janelas usam esses recursos, então a troca de tema vale na hora.

## Grade de miniaturas

O Professor abre com a **grade da sala**: uma tela por computador, atualizando 2 vezes por segundo (leve para a
rede). **Duplo clique** em uma tela abre ela em tamanho grande, a ~55 fps; Esc volta.

- **Computadores...** — monta a lista da sala (adicionar, editar, remover, mudar a ordem) ou **Procurar na rede...**.
- Cada agente se anuncia na rede (`--rede`) e aparece na busca. Para testar no mesmo PC, abra dois agentes:

```bash
ControleSala.Agente.exe --porta 47810 --anunciar 127.0.0.1
ControleSala.Agente.exe --porta 47811 --anunciar 127.0.0.1
```

- Se um computador estiver desligado, a tela dele fica vermelha ("Offline") e o programa tenta de novo a cada 3 s.
- Opções do agente para as miniaturas: `--miniatura-largura 480`, `--miniatura-fps 2`, `--miniatura-kbps 600`.

## Enviar arquivos

No Professor, **Enviar arquivos...**: escolha os arquivos, marque os computadores (só os online) e clique em **Enviar**.
Cada computador mostra o andamento; vários computadores recebem ao mesmo tempo. Os arquivos chegam na pasta
**"Arquivos do Professor"**, na área de trabalho de todos os usuários do PC do aluno (eles podem abrir, alterar e apagar),
e aparece um aviso perto do relógio. Enviar de novo um arquivo com o mesmo nome substitui o anterior.

- O arquivo só aparece para o aluno **depois de chegar inteiro e ter o SHA-256 conferido**; cancelar ou perder a
  conexão não deixa arquivo pela metade.
- **Segurança:** o agente confere tudo de novo, mesmo que o professor mande errado: nome sem `\ / : * ? " < > |`,
  sem caracteres invisíveis, sem nomes reservados do Windows (CON, NUL...), tamanho até 2 GB e espaço livre no disco
  (sempre sobram 512 MB). **Só passam tipos seguros** (lista de permitidos): documentos (.pdf, .doc/.docx,
  .xls/.xlsx, .ppt/.pptx, .odt/.ods/.odp, .rtf, .txt, .csv, .md, .epub), imagens, áudio, vídeo e compactados
  (.zip, .rar, .7z). Qualquer outro tipo — programas, scripts, atalhos e tipos novos — é recusado por padrão.
  O envio vai pelo mesmo canal autenticado e criptografado do vídeo.
- Sem janela (testes/automação): `ControleSala.Professor.exe --enviar <arquivo>... --para <host:porta> [--resultado <arquivo>]`.
- Medido no notebook (agente e professor no mesmo PC): 300 MB em ~3 s.
- Para testar sem administrador: `ControleSala.Agente.exe --pasta-recebidos <pasta> ...`.

## Papel de parede da sala

No Professor, botão **Papel de parede...**: escolha o fundo e o logotipo da escola, a posição e o tamanho, veja a
prévia e salve o PNG. Também dá para gerar sem janela:

```bash
ControleSala.Professor.exe --gerar-papel-de-parede saida.png [--config arquivo.json]
```

## Professores (PIN)

Ao abrir o Professor, cada professor entra com nome e PIN (4 a 8 números). Em **Professores...** dá para adicionar,
trocar PIN e remover. Depois de 5 PINs errados o programa bloqueia por 30 s (e vai dobrando até 5 min).

Opções extras do Professor: `--dados <pasta>` (usa outra pasta de configurações) e
`--capturar <janela> <saida.png> [--esperar <segundos>]` (desenha uma janela numa imagem, para conferir o visual;
janelas: `principal`, `tela`, `computadores`, `editar`, `procurar`, `papel`, `entrar`, `primeiro`, `novo`,
`trocarpin`, `confirmar`, `professores`, `sobre`).

Testes automáticos:

```bash
dotnet test src/ControleSala.Testes
```

## Desempenho

A tela cheia transmite a **30 fps** por padrão — suave para monitorar e controlar a sala, usando metade da rede e da CPU em relação a 60 fps. Dá para mudar com `--fps` no agente (1 a 120). As miniaturas da grade usam 2 fps.

## Resultados da prova de conceito (i5-8250U, Intel UHD 620, 1366x768)

| Medida | Valor |
|---|---|
| Encoder QuickSync sozinho | 123 fps (1366x768), 100 fps (1080p) |
| Transmissão com movimento contínuo | 50–55 fps, ~6–7 Mbps |
| Atraso no agente (captura → envio) | ~9–12 ms |
| Atraso total até a tela do professor | ~25–28 ms (agente e professor no mesmo notebook) |
