# Roteiro do ControleSala

## Próximos passos (em ordem)

1. ~~**Controle remoto**~~ — **feito (2026-10-14)**: botão **"Controlar"** na tela cheia envia mouse (mover/clicar/roda) e
   teclado ao agente, que injeta na sessão do aluno (`SendInput`). Mensagem `Entrada` (12) no protocolo (coordenadas
   normalizadas 0..65535); `EntradaRemota.cs` no agente; `IndicadorControle.cs` mostra a faixa **"Prof. X está controlando
   este computador"** (transparência). Só pelo canal autenticado da sala. **Decisão:** por enquanto **não trava** o mouse/teclado
   do aluno (os dois mexem) — dá para adicionar a trava depois. Esc sai do controle. Build + 8 testes do protocolo; a injeção
   real precisa de teste nos 2 PCs (não dá para testar daqui). Pendente de ideia antiga: desenhar o cursor do professor sobre a transmissão.
   **2026-10-14 (cont.):** botão **"Travar aluno"** (trava o mouse/teclado físico do aluno durante o controle, via `BlockInput`;
   `TravaAluno.cs` com watchdog que destrava sozinho se o professor cair; o professor reenvia "travar" a cada 2 s; Ctrl+Alt+Del
   do aluno continua funcionando). A **versão** do programa aparece discreta no rodapé (barra de status e tela cheia).
2. **Segurança** — **núcleo feito (2026-10-10)**: chave da sala, pacote com senha, aperto de mão com prova da chave
   nos dois sentidos, canal AES-256-GCM, perfis de professor, nome do professor no ícone do aluno (ver
   "Instalador e segurança da sala" abaixo). **Falta:** log de auditoria (quem viu/fez o quê), limite de tentativas
   por endereço no agente, trocar/girar a chave da sala, assinar o instalador. Pré-requisito para qualquer comando
   que altere o PC do aluno (papel de parede, limpeza etc.) — esses comandos devem exigir o canal autenticado.
3. ~~**Grade de miniaturas**~~ — **feita (2026-10-10)**, ver "Grade de miniaturas" abaixo. Falta: arrastar as telas para a
   posição das mesas (hoje a ordem é a da lista, com ▲▼).
4. ~~**60 fps estáveis**~~ — **resolvido de outro jeito (2026-10-12)**: medição mostrou que a decodificação por software já faz ~339 fps (hardware seria MAIS lento aqui); o gargalo era rodar tudo num PC só. Decisão do Diego: baixar o padrão para **30 fps** (suave para monitorar/controlar, metade da rede e da CPU). `--fps` ainda ajusta.
5. ~~**Comandos**~~ — **feitos (2026-10-11/12)**: link, mensagem, bloquear/desbloquear tela, abrir/fechar programa, desligar/reiniciar/logoff, silenciar, Wake-on-LAN, bloquear internet/sites. Ver README.
6. ~~**Limpeza automatizada**~~ — **feita (2026-10-12)**: configurável (o que apagar, exceções, imagem, papel de parede) e **agendada** (dias+horário) + Limpar agora. Ver README.
   **Atualizada (2026-10-14):** o agendamento saiu da Tarefa do Windows (rodava como SYSTEM na sessão 0, invisível) e passou
   para o **próprio agente em sessão** (`AgendadorLimpeza`), que checa a cada 30 s se está na hora (lógica pura testada em
   `AgendaLimpeza`), mostra o **aviso ao aluno** (contagem regressiva + "Limpar agora" / "Adiar 1 hora"; adiar = +1 h) e,
   seguindo, mostra a **tela de carregamento** e limpa. `ConfigLimpezaAgente` só salva o `limpeza.json` e apaga a tarefa
   antiga. O teste real na escola (roteiro) passou dentro do previsto.
7. ~~**Transferir arquivos**~~ — **feito (2026-10-11)**: professor → alunos (ver README "Enviar arquivos").
   **Teste real com administrador passou:** com o agente instalado como serviço, o arquivo chegou em
   `C:\Users\Public\Desktop\Arquivos do Professor` (acentos certos; a pasta tem Usuários:Modificar para os alunos). Falta: mandar
   pastas inteiras, e o caminho inverso (recolher trabalhos dos alunos), que precisa de desenho próprio de permissões.
8. ~~**Atualização**~~ — **feita (2026-10-12)**: o Setup novo instala por cima mantendo tudo; e o professor atualiza os
   alunos pela rede (instalador com assinatura do autor, só versão mais nova). Ver README "Atualizar". Teste com
   administrador (0.2.99 → 0.3.0 pela rede) PASSOU. Falta: o backup da chave de assinatura pelo Diego.

## Melhorias de interface (2026-10-14, pedido do Diego depois do teste real)

- ~~**Selecionar computadores na grade**~~ — **feito**: clique (um), **Ctrl+clique** (vários), **Shift+clique** (intervalo),
  **Ctrl+A** (todos), **Esc** (limpa), clique no vazio limpa. Contorno azul + selo de marcado; "X selecionados" na barra de
  status. **Botão direito** abre um menu com as ações (ver tela, link, mensagem, arquivos, bloquear, silenciar, internet,
  desligar/reiniciar, limpar) agindo só nos marcados (direito num não-marcado seleciona só ele). Todos os comandos e o enviar
  arquivos respeitam a seleção (nada marcado = todos, como antes). Verificado por captura fora da tela.
- ~~**Barra de ferramentas com mais ações**~~ — **feito**: + Mensagem, Bloquear tela, Desligar; rola na horizontal se faltar espaço.
- ~~**Cores da limpeza**~~ — **feito**: janela em cartões; botões dos dias da semana com o visual do programa (azul quando ligados),
  via estilo global de `ToggleButton`.
- ~~**Tela de aviso + carregamento da limpeza**~~ — **feito** (ver item 6): visual do programa (cartão escuro, azul, logo da
  escola, barra azul própria, crédito discreto). Conferidas por imagem (`--previa-limpeza`).
- ~~**Controle remoto (mouse/teclado)**~~ — **feito (2026-10-14)**, ver item 1 acima.
- ~~**Arrastar e soltar o .controlesala no instalador**~~ — **feito (2026-10-14)**: além do botão Procurar (que já existia), dá
  para arrastar o `.controlesala` do pendrive e soltar na janela do instalador (`ControleSala.iss`: WM_DROPFILES + liberação
  do UIPI por rodar elevado + dica na página). Precisa ser validado ao gerar o Setup (não há ISCC no ambiente de dev atual).
- ~~**Atualização online "num clique"**~~ — **já existia** (faixa de aviso → `JanelaNovaVersao`); em 2026-10-14 virou botão único
  **"Atualizar agora"** (baixa do GitHub + confere assinatura + instala por cima, mantendo tudo). "Enviar aos alunos" baixa sob demanda.
- ~~**Travar o mouse/teclado do aluno durante o controle**~~ — **feito (2026-10-14)**, ver item 1.
- **Falta (ideias futuras):** arrastar as miniaturas para a posição das mesas; log de auditoria (LGPD); cursor do professor
  desenhado sobre a transmissão.

## Instalador e segurança da sala (feito 2026-10-10)

- **Ordem trocada de propósito:** o instalador do aluno liga o agente sozinho e aceita conexão da rede; sem chave e
  criptografia isso exporia as telas. Por isso o núcleo de segurança veio junto com o instalador.
- `instalador\ControleSala.iss` (Inno Setup 6) + `instalador\gerar-instalador.ps1`. Detalhes de uso no README.
- **Agente como serviço:** o serviço `ControleSalaAgente` (SYSTEM) abre o agente (`--sessao`) na sessão de quem está
  no PC, com o token do próprio serviço — o aluno não encerra nem abre. Reabre ao trocar de usuário e se o agente cair.
- **Ícone da bandeja:** cartão informativo (sala, escola, quem está vendo, crédito), sem menu nem botões.
- **Teste real com administrador (2026-10-11) — PASSOU:** Setup silencioso como Aluno no notebook do Diego:
  serviço `ControleSalaAgente` rodando (início automático); o agente roda como **SYSTEM na sessão do usuário**
  (um processo de serviço na sessão 0 + o agente na sessão 2) — aluno comum não consegue encerrar; regra de firewall
  só TCP 47810 e só sub-rede local; pasta `%ProgramData%\ControleSala` só com Administradores e SYSTEM; o agente exige
  a chave (manda o desafio ao conectar); o Professor conectou com a chave e recebeu as miniaturas (QuickSync funciona
  como SYSTEM); a desinstalação removeu serviço, regra, pastas (inclusive a chave) e encerrou o agente.
  **Ainda sem confirmação visual:** o ícone da bandeja (o Diego precisa olhar perto do relógio / seta ^).
- **Testado sem administrador:** lógica (73 testes), agente com chave (certo/errado/sem chave/intruso), pacote,
  janelas, e o **Setup silencioso** (`/CURRENTUSER`): Professor instala e abre; Aluno com senha errada encerra sem
  janela (código 1) e sem instalar; Aluno com senha certa copia só o agente; desinstalação remove a pasta.
  Bugs achados e corrigidos nesses testes: (1) publicar por cima de pasta antiga faz o SDK apagar as DLLs laterais
  (WPF/FFmpeg) — o script agora publica em pasta limpa e confere os arquivos; (2) no modo silencioso o Inno abria
  janelas de mensagem e travava — agora `Abort` sem janela. **Precisa de teste real com administrador (UAC):** criação do serviço, abrir o agente na sessão do aluno,
  ícone da bandeja como SYSTEM, regra de firewall, desinstalação.
- **Falta:** ~~GitHub Actions para gerar o Setup~~ (feito 2026-10-12); assinatura do instalador (SmartScreen avisa); teste em
  rede real com 2 PCs; atualização automática do agente nos alunos.

## Instalador (pedido do Diego, 2026-10-09)

Um único `ControleSala-Setup.exe` feito com **Inno Setup** (gratuito, assistente clássico em português, desinstalador
em "Adicionar ou remover programas", modo silencioso), gerado automaticamente pelo GitHub Actions.

Páginas: Bem-vindo (crédito discreto) → **Tipo: Professor ou Aluno** → [Aluno: arquivo da sala] → **Pasta de destino**
(padrão `C:\Program Files\ControleSala`, botão Procurar) → Atalhos → Pronto para instalar → Concluído.

- Precisa de administrador (aluno com conta comum não instala nem desinstala).
- Aluno: instala o agente como **serviço do Windows** (inicia com o Windows), regra de firewall só para a rede local
  e ícone na bandeja mostrando a sala e quando a tela está sendo vista.
- Professor: instala o console com atalho na área de trabalho e no menu Iniciar.
- Modo silencioso para muitos PCs: `ControleSala-Setup.exe /VERYSILENT` (pega o arquivo da sala ao lado do Setup).

## Configuração da sala

**PC do professor (uma vez):** instala como Professor → no 1º uso, assistente "Configurar sala" (escola, nome da sala,
logo/papel de parede opcionais) → o programa cria a **chave da sala** → botão **"Gerar pacote para os alunos"**
(pasta com o Setup + arquivo `.controlesala` da sala, para o pendrive) → **backup da chave** com senha
(se formatar o PC do professor, restaura sem reinstalar os alunos).

**PC de cada aluno (uma vez):** pendrive → Setup → o instalador acha o arquivo da sala ao lado e mostra
"Este computador fará parte da sala X da escola Y" → Avançar → Instalar. O PC aparece sozinho no console do professor;
o professor arrasta para a posição da mesa e dá um nome ("Mesa 05").

O agente só obedece a consoles com a chave da sua sala: o professor da sala A não controla a sala B.

### Decisões (respostas do Diego, 2026-10-09)

- **Dois professores usam o mesmo PC do professor** → a chave da sala fica no PC (não no usuário). O console terá
  **perfis de professor** (nome + PIN): ao abrir, escolhe "quem está usando". O log registra qual professor fez o quê
  e o aviso no PC do aluno mostra o nome do professor.
- **PCs dos alunos têm duas contas:** administrador (com senha) e "aluno" (sem senha, a que os alunos usam).
  Instalar, configurar e desinstalar fica só com o administrador — é o comportamento normal do Windows para
  programas instalados por ele. A limpeza atua só na conta "aluno", nunca na de administrador.
- **Ícone na bandeja (perto do relógio) no PC do aluno:** sempre visível. Na conta "aluno", clicar nele mostra só um
  cartão informativo (sala, escola, "sua tela está sendo vista por Prof. X" quando for o caso, crédito discreto);
  sem configurações. Configurar só com a senha de administrador do Windows.

## Grade de miniaturas (feito 2026-10-10)

- **Agente com dois fluxos:** *miniatura* (480x270, 2 fps, todos os quadros são chave, ~0,6 Mbps) e *completo* (tela
  inteira, ~55 fps, ~6–7 Mbps). O professor escolhe o modo por conexão (`DefinirModo`); o agente só liga o fluxo que
  alguém pediu e desliga 5 s depois que ninguém usa. Sem professor conectado, não captura nem codifica nada.
  Medido (UHD 620): 3 professores ao mesmo tempo (1 completo + 2 miniaturas) = agente com ~29% de **um** núcleo.
- **Professor:** grade com uma tela por computador (nome, bolinha verde/amarela/vermelha, "Offline — motivo"),
  controle de tamanho, **duplo clique abre a tela grande** (modo completo; Esc fecha e volta à miniatura),
  menu de contexto (Ver tela / Reconectar). Cada conexão **reconecta sozinha** a cada 3 s.
- **Lista da sala** (`%AppData%\ControleSala\sala.json`): janela "Computadores..." (adicionar, editar, remover, ▲▼).
- **Descoberta:** o agente anuncia nome+porta por UDP (porta 47811) a cada 3 s — broadcast com `--rede`, ou direto para
  um IP com `--anunciar <ip>`. "Procurar na rede..." escuta 6 s e oferece os PCs novos. Não há varredura de portas.
- Testes: `dotnet test` (51 testes: perfis, sala, descoberta, protocolo). Visual e reconexão conferidos com o
  agente real (2 agentes + 1 "desligado").
- **Pendências desta parte:**
  - FPS exibido na tela grande (~36 fps) fica abaixo do enviado (~57): a decodificação e a conversão de cor são na CPU;
    a solução é decodificar por hardware (D3D11VA/QSV) e converter a cor na GPU. Medir de novo em PCs separados.
  - O broadcast `255.255.255.255` sai só pela placa de rede principal; em PC com várias placas, anunciar por placa.
  - O anúncio ainda não é autenticado (qualquer um na rede pode anunciar um nome); some quando a segurança chegar.

## Perfis dos professores (feito 2026-10-10)

- Ao abrir o Professor: **primeiro acesso** (cadastra o 1º professor) ou **"Quem está usando?"** (nome + PIN de 4 a 8 números).
- PIN guardado só como resumo PBKDF2-SHA256 (600 mil voltas, sal aleatório); 5 erros bloqueiam por 30 s, dobrando
  a cada novo erro até 5 min; o bloqueio vale mesmo fechando e abrindo o programa.
- Botões na barra: **Trocar** (outro professor entra), **Professores...** (adicionar/remover pedem o PIN de quem está
  usando; trocar PIN pede o PIN atual do professor), **Sobre**.
- Arquivo: `%AppData%\ControleSala\professores.json`. O PIN protege contra uso casual no PC do professor; quem tem
  administrador nesse PC pode apagar o arquivo.
- `Sessao.Atual` já guarda quem está usando — será usado no aviso ao aluno e no histórico.
- Testes automáticos: `dotnet test src/ControleSala.Testes` (24 testes da lógica de perfis).

## Aparência (feito 2026-10-11)

Professor com **menu, barra de ferramentas (ícones), barra de status** e **dois temas (claro/escuro)** com troca
guardada. Toda menção a "JQSoluçõesTI" foi removida (a pedido do Diego) — o crédito fica só "criado pelo professor
Diego Sabino Delfino". Detalhes no README ("Aparência"). Falta: arrastar as telas para a posição das mesas.

## Ideias registradas

### Papel de parede padrão + logotipo da escola (ideia do Diego, 2026-10-09)

**Feito (2026-10-10):** botão **"Papel de parede..."** no Professor. Escolhe fundo, logo (PNG), posição (9 lugares),
tamanho, resolução (1920x1080, 1366x768, 1600x900, 1280x1024) e crédito discreto; prévia ao vivo; salva PNG.
Configuração em `%AppData%\ControleSala\papel-de-parede.json`. Sem janela:
`ControleSala.Professor.exe --gerar-papel-de-parede saida.png [--config arquivo.json]`.
**Falta:** enviar a imagem aos agentes e restaurá-la na limpeza (depende da camada de segurança).

- No console do professor, uma tela **"Papel de parede da sala"**:
  - escolher a **imagem de fundo** padrão;
  - escolher o **logotipo da escola** (PNG, de preferência com fundo transparente);
  - escolher **onde o logo fica** (cantos ou centro) e o tamanho;
  - **pré-visualização** da imagem final.
- O console junta fundo + logo numa imagem só e envia para os agentes.
- **Na limpeza**, o agente restaura esse papel de parede (fundo + logo da escola).
- Também dá para aplicar o papel de parede a qualquer momento, sem limpar nada.
- Cada sala/professor guarda a sua configuração (o programa vai ser distribuído para outras escolas).

### Créditos "criado pelo professor Diego Sabino Delfino" (sempre discretos)

- [x] Barra inferior do Professor (texto pequeno, cinza).
- [x] Cabeçalho do Agente (linha cinza).
- [x] Propriedades do .exe (Detalhes → Autor/Copyright).
- [x] Tela "Sobre" do Professor.
- [ ] Aviso de limpeza mostrado no PC do aluno (linha pequena no rodapé do aviso).
- [ ] Instalador (página de boas-vindas).
- [ ] Opcional: linha minúscula num canto do papel de parede padrão (o professor pode desligar).

Os textos ficam todos em `src/ControleSala.Comum/Creditos.cs`, num lugar só.
