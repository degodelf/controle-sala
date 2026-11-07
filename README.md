# ControleSala

Substituto do Veyon para a sala de informática: o professor vê e controla os PCs dos alunos,
com transmissão de tela em vídeo H.264 (QuickSync/GPU) em vez de VNC.

> **Estado atual:** em uso em uma sala real. Grade de telas, controle remoto, envio de arquivos, comandos, limpeza
> automática, atualização pela rede e conexão **autenticada e criptografada** (ver `ROADMAP.md`). Sem chave da sala
> o agente só aceita conexões do próprio PC. Privacidade: ver "Privacidade e LGPD" abaixo.

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
[Aluno: nome do PC + arquivo da sala + senha] → Pasta de instalação → Instalar → Concluir**, e aparece em
"Adicionar ou remover programas".

- **Professor:** instala o programa de controle, atalhos e a liberação do firewall para a busca na rede.
- **Aluno:** instala o agente como **serviço do Windows** (liga com o PC, reinicia se cair, roda como SYSTEM —
  o aluno não consegue encerrar nem abrir), grava a chave da sala numa pasta só de administrador, libera o
  firewall **só para a rede local** e coloca o ícone informativo perto do relógio. Não cria atalho nenhum.
  A tarefa agendada **ControleSala Vigia** tenta ligar o serviço a cada minuto, porque o Windows não religa sozinho
  um serviço que demorou demais para subir. Para parar o serviço de propósito, desative também a tarefa.
- **Nome do PC:** o nome digitado na instalação (ex.: `PC 01`) fica em `%ProgramData%\ControleSala\nome-pc.txt` e vale
  só no ControleSala. O agente anuncia esse nome e o envia ao professor, que renomeia o PC na lista e reordena. Se o
  professor renomear depois, a escolha dele fica, até alguém escolher outro nome no PC.
- Silencioso: `ControleSala-Setup-0.2.0.exe /VERYSILENT /TIPO=aluno /SALA="E:\Lab1.controlesala" /SENHA=... /NOME="PC 01"`
  (sem `/SALA` procura um `.controlesala` ao lado do Setup; sem `/SENHA` usa a variável `CONTROLESALA_SENHA`).
- Teste sem administrador: `/CURRENTUSER` instala os arquivos mas **não** cria serviço nem regras de firewall.
- Instalação silenciosa com chave/senha erradas não abre janela: encerra com código de saída diferente de 0
  (veja o motivo com `/LOG=arquivo.log`).
- Desinstalar (PC de aluno) também libera internet/sites que estavam bloqueados e remove a limpeza agendada
  (o desinstalador chama `ControleSala.Agente.exe --desfazer`).
- Sempre gere o instalador com `instalador\gerar-instalador.ps1`: ele publica em pasta limpa (publicar por cima
  de uma pasta antiga faz o SDK apagar as DLLs do WPF e do FFmpeg).

## Atualizar

Rode o Setup da versão nova **por cima** da instalada (mesmo `AppId`): os arquivos são trocados e o resto fica.

- Atualizações: o programa lê `/releases/latest` de `degodelf/controle-sala` sem login. Com o repositório privado, os
  programas não veem versão nova. Antes de fechar o código, publique uma versão apontando para um repositório público só com
  os instaladores (`VerificadorAtualizacao.Repositorio`).
- O instalador **lembra o tipo** do computador (professor/aluno) e não pergunta de novo. A 0.1.0 não guardava o
  tipo: nesse caso ele descobre pelo programa que está na pasta instalada.
- **PC de aluno:** se já tem sala gravada, oferece **"Manter a sala atual"** (padrão) e não pede chave nem senha;
  escolha "Trocar de sala" só se o PC mudou de sala. O serviço é parado antes e religado depois.
- **PC do professor:** sala, chave, professores, papel de parede, limpeza etc. ficam em `%AppData%\ControleSala` e não
  são tocados. Se o programa estiver aberto, o instalador pede para fechar. PC do professor formatado: depois de **Inserir
  chave**, a agenda e as exceções da limpeza voltam sozinhas do primeiro PC de aluno que conectar.
- Silencioso (vários PCs): `ControleSala-Setup-0.3.0.exe /VERYSILENT` — sem `/TIPO`, `/SALA` nem `/SENHA`.
  Com `/SALA=... /SENHA=...` troca a sala junto.
- Testado com administrador (2026-10-12): 0.1.0 aluno → 0.2.0 silencioso, serviço parado e religado, sala mantida,
  desinstalação limpa (inclusive a regra de bloqueio de internet).

### Atualizar os alunos pela rede (a partir da 0.3.0)

No uso real: o autor gera o Setup novo (assinado) → o professor instala por cima no PC dele → no programa,
**Computadores → Atualizar o ControleSala dos alunos...** (ou o aviso "⚠ N computadores com versão antiga" na barra de
status; PC que não informa a versão conta como desatualizado) → marca os desatualizados → **Atualizar**. Cada PC recebe o instalador pela conexão da sala, confere e se
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
   pelo Windows (DPAPI) neste PC. **Renomear...** troca só os nomes: a chave continua a mesma, os computadores seguem
   conectados e o arquivo da chave já gerado continua valendo.
2. **Gerar chave da sala...** grava um `.controlesala` protegido por uma **senha** escolhida na hora
   (PBKDF2-SHA256 600 mil voltas + AES-256-GCM). Leve no pendrive e apague depois.
3. O instalador (opção Aluno) pede o arquivo da chave e a senha, confere e grava a chave em `%ProgramData%\ControleSala`
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

Entre dois PCs (sem instalar): no Professor crie a sala e gere a chave; no PC do aluno rode
`ControleSala.Agente.exe --rede --sala <arquivo.controlesala> --senha <senha>` (o Windows vai pedir para liberar no firewall);
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

**Bloquear a internet / sites:** Comandos tem três opções. **Bloquear TODA a internet** corta o acesso à internet,
em IPv4 e IPv6 (a rede local e o ControleSala continuam funcionando), via regra de firewall. **Bloquear sites da lista** bloqueia só
os sites de **Sites bloqueados...** (ex.: redes sociais), via arquivo hosts, deixando o resto funcionar.
**Liberar a internet** desfaz os dois. Precisa do agente como serviço.

**Limpeza automatizada:** Comandos → Limpeza automática (configurar). Os arquivos são apagados com as permissões do
aluno, não como SYSTEM, e atalhos de pasta (junções) não são seguidos. O professor escolhe o que apagar na conta do
aluno (Downloads, Área de Trabalho, Documentos, histórico/cache/cookies dos navegadores, Lixeira, %TEMP%), exceções
(pastas/arquivos a preservar), se **restaura o papel de parede** (fundo+logo) e a **agenda** (dias da semana + horário).
A tela de limpeza mostra o **logotipo da escola** escolhido em Papel de parede (sem logotipo, o ícone do programa).
Também pode **atualizar o Windows Defender** e **fazer uma verificação** (rápida ou completa) depois da limpeza: o Defender
roda em segundo plano, a verificação acontece mesmo se a atualização falhar, e o resultado de cada PC aparece em
**Computadores > Situação do Windows Defender**. **Pausar** / **Retomar**, no alto da janela, vale na hora para os
computadores online e guarda a agenda; PC desligado só recebe a pausa quando conectar ao professor. A configuração e as
imagens são enviadas aos computadores quando ficam online, e o próprio agente roda a agenda, mesmo sem o professor
conectado. Ao terminar, cada PC informa o resultado: a barra de status do professor mostra um aviso discreto com o link do
**relatório** em `.html`, com um gráfico de barras em GB por computador e o total no final, e **Comandos > Relatórios de
limpeza** lista as 5 limpezas mais recentes (a mais antiga é apagada quando chega uma nova). Também há **Limpar agora**, que funciona mesmo com a agenda pausada: pede confirmação ao professor e dá 30 segundos de aviso ao aluno. **A limpeza nunca roda
no PC do professor** (ele só tem o console, não o agente) e nunca mexe na conta de administrador.

## Licença

Licença própria, em `LICENSE.txt`: escolas, redes de ensino e secretarias de educação usam de graça, ninguém pode vender, e o
crédito ao autor deve continuar no programa. O instalador mostra o texto na primeira instalação e o copia como `LICENCA.txt`.
O FFmpeg e os demais componentes continuam com as próprias licenças.

## Privacidade e LGPD

O programa mostra telas de alunos, muitos deles crianças e adolescentes. O que foi feito para isso:

- **Entrada:** o programa abre sem senha. Cada professor se identifica pelo nome e aceita as regras de uso uma vez; com um só
  professor no PC, abre direto. O registro mostra o nome escolhido, sem confirmar quem estava no PC.
- **Registro de atividades (Ajuda):** quem fez o quê, quando e em qual computador, só no PC do professor
  (`%AppData%\ControleSala\atividades`), por 6 meses, com exportação para planilha. Não guarda imagens, texto de mensagens
  nem a configuração da limpeza.
- **O aluno sabe:** o ícone perto do relógio fica vermelho com o nome do professor; com a tela aberta em tamanho grande, ou
  sob controle, aparece uma faixa no topo.
- **Tentativas:** 5 erros seguidos de chave da sala bloqueiam aquele endereço por 1 minuto.
- **O que o programa não faz:** não grava tela nem áudio, não usa câmera nem microfone, não registra o que é digitado e não
  tem cadastro de aluno. As telas não saem da rede da escola.
- **Mudar:** `Entrada.VersaoDasRegras` faz todos aceitarem as regras de novo.

Isso é a parte técnica. Base legal, relatório de impacto e aviso às famílias dependem da SED, que é a controladora dos dados.

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
- Os PCs da sala entram na lista **sozinhos**: cada agente se anuncia na rede a cada 3 s e assina o anúncio com a chave da
  sala, então o programa do professor põe na lista só os da própria sala, em ordem pelo nome, sem pegar outro laboratório da
  mesma rede. Se um IP mudar, mesmo que dois PCs troquem de endereço, o PC é reconhecido pelo nome que anuncia e a lista se
  acerta sozinha, ainda que tenha sido renomeado nela. Antes de mudar a lista, o programa se conecta ao endereço anunciado e
  confere a chave da sala e o nome do PC: copiar o aviso de outro PC não adianta. PC removido pelo professor não volta sozinho. **Procurar na rede...**
  continua valendo para trazer um removido,
  para PCs com versão antiga e para dois PCs com o mesmo nome.
- Cada agente se anuncia na rede (`--rede`) e aparece na busca. Para testar no mesmo PC, abra dois agentes:

```bash
ControleSala.Agente.exe --porta 47810 --anunciar 127.0.0.1
ControleSala.Agente.exe --porta 47811 --anunciar 127.0.0.1
```

- Se um computador estiver desligado, a tela dele fica vermelha ("Offline") e o programa tenta de novo a cada 3 s.
- Opções do agente para as miniaturas: `--miniatura-largura 480`, `--miniatura-fps 2`, `--miniatura-kbps 600`.

## Enviar arquivos

No Professor, **Enviar arquivos...**: escolha os arquivos, marque os computadores (só os online) e clique em **Enviar**.
Marque **Abrir o arquivo no computador do aluno assim que chegar** para ele abrir sozinho, no programa padrão do PC, na conta
do aluno.
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

## Professores

Ao abrir o Professor, com mais de um professor no PC, o programa pergunta quem está usando; com um só, abre direto. Em
**Professores...** dá para adicionar e remover professores.

Opções extras do Professor: `--dados <pasta>` (usa outra pasta de configurações) e
`--capturar <janela> <saida.png> [--esperar <segundos>]` (desenha uma janela numa imagem, para conferir o visual;
janelas: `principal`, `tela`, `computadores`, `editar`, `procurar`, `papel`, `entrar`, `primeiro`, `novo`,
`professores`, `sobre`).

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
