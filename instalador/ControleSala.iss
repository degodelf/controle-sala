; ControleSala — instalador (Inno Setup 6)
; Criado pelo professor Diego Sabino Delfino
;
; Para gerar o ControleSala-Setup:  veja instalador\gerar-instalador.ps1
; Instalação silenciosa (vários PCs):
;   Professor:  ControleSala-Setup-0.1.0.exe /VERYSILENT /TIPO=professor
;   Aluno:      ControleSala-Setup-0.1.0.exe /VERYSILENT /TIPO=aluno /SALA="E:\Laboratorio1.controlesala" /SENHA=senha-do-pacote
;               (sem /SALA, procura um .controlesala na pasta do Setup; sem /SENHA, usa a variável CONTROLESALA_SENHA)
; Atualizar (Setup novo por cima do antigo): ControleSala-Setup-x.y.z.exe /VERYSILENT
;   lembra se o PC é de professor ou de aluno e mantém a sala, os professores e as configurações.

#define Nome "ControleSala"
; A versão vem do Directory.Build.props (o gerar-instalador.ps1 passa /DVersao=...).
#ifndef Versao
  #define Versao "0.0.0"
#endif
#define Editora "Prof. Diego Sabino Delfino"
#define Autor "Diego Sabino Delfino"
#define ExeProfessor "ControleSala.Professor.exe"
#define ExeAgente "ControleSala.Agente.exe"
#define PastaPublicar "..\publicar"
#define PastaFFmpeg "..\terceiros\ffmpeg"

[Setup]
AppId={{CEAD6F19-D73A-4CF4-9684-D09895597A64}
AppName={#Nome}
AppVersion={#Versao}
AppVerName={#Nome} {#Versao}
AppPublisher={#Editora}
AppCopyright=Criado pelo professor {#Autor}
DefaultDirName={autopf}\{#Nome}
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile=controlesala.ico
UninstallDisplayIcon={app}\controlesala.ico
OutputDir=saida
OutputBaseFilename=ControleSala-Setup-{#Versao}
Compression=lzma2/max
SolidCompression=yes
ShowLanguageDialog=no
LanguageDetectionMethod=none
VersionInfoVersion={#Versao}
VersionInfoCompany={#Editora}
VersionInfoDescription=Instalador do {#Nome}
VersionInfoCopyright=Criado pelo professor {#Autor}

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Messages]
ptbr.WelcomeLabel2=Este assistente vai instalar o [name/ver] neste computador.%n%nVocê vai escolher se este é o computador do professor ou de um aluno.%n%nFeche os outros programas antes de continuar.%n%nCriado pelo professor {#Autor} · {#Editora}

[Tasks]
Name: "iconedesktop"; Description: "Criar um atalho na área de trabalho"; Check: EhProfessor

[Files]
; Bibliotecas de vídeo (FFmpeg, LGPL) e o ícone: valem para os dois tipos de computador.
Source: "{#PastaFFmpeg}\*"; DestDir: "{app}"; Flags: ignoreversion
Source: "controlesala.ico"; DestDir: "{app}"; Flags: ignoreversion

; Professor: o programa de controle (WPF) e as bibliotecas dele.
Source: "{#PastaPublicar}\Professor\*"; DestDir: "{app}"; Excludes: "avcodec*.dll,avutil*.dll,swresample*.dll,swscale*.dll,LICENSE-FFmpeg-LGPL.txt"; Flags: ignoreversion recursesubdirs; Check: EhProfessor

; Aluno: o agente. Vem da pasta temporária porque o instalador precisa dele antes (para conferir a senha do pacote).
Source: "{#PastaPublicar}\Agente\{#ExeAgente}"; DestDir: "{tmp}"; Flags: dontcopy
Source: "{tmp}\{#ExeAgente}"; DestDir: "{app}"; Flags: external ignoreversion; Check: EhAluno

[Icons]
Name: "{autoprograms}\ControleSala (Professor)"; Filename: "{app}\{#ExeProfessor}"; IconFilename: "{app}\controlesala.ico"; Check: EhProfessor
Name: "{autodesktop}\ControleSala"; Filename: "{app}\{#ExeProfessor}"; IconFilename: "{app}\controlesala.ico"; Tasks: iconedesktop; Check: EhProfessor

[Run]
Filename: "{app}\{#ExeProfessor}"; Description: "Abrir o ControleSala agora"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: EhProfessor

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
const
  NomeServico = 'ControleSalaAgente';
  RegraAgente = 'ControleSala Agente (video)';
  RegraProfessor = 'ControleSala Professor (busca na rede)';
  PortaVideo = '47810';
  PortaBusca = '47811';

function SetEnvironmentVariable(Nome, Valor: String): Boolean;
  external 'SetEnvironmentVariableW@kernel32.dll stdcall';

{ --- Arrastar e soltar o arquivo .controlesala na janela do instalador --- }
const
  GWL_WNDPROC = -4;
  WM_DROPFILES = $0233;
  WM_COPYGLOBALDATA = $0049;
  MSGFLT_ALLOW = 1;

function CallWindowProc(lpPrevWndFunc: LongInt; hWnd: HWND; Msg: Cardinal; wParam: LongInt; lParam: LongInt): LongInt;
  external 'CallWindowProcW@user32.dll stdcall';
function SetWindowLong(hWnd: HWND; nIndex: Integer; dwNewLong: LongInt): LongInt;
  external 'SetWindowLongW@user32.dll stdcall';
function ChangeWindowMessageFilterEx(hWnd: HWND; Msg: Cardinal; Action: Cardinal; Estrutura: LongInt): Boolean;
  external 'ChangeWindowMessageFilterEx@user32.dll stdcall';
procedure DragAcceptFiles(hWnd: HWND; Aceita: Boolean);
  external 'DragAcceptFiles@shell32.dll stdcall';
function DragQueryFile(hDrop: LongInt; iArquivo: Cardinal; lpszArquivo: String; cch: Cardinal): Cardinal;
  external 'DragQueryFileW@shell32.dll stdcall';
procedure DragFinish(hDrop: LongInt);
  external 'DragFinish@shell32.dll stdcall';

var
  PaginaTipo: TInputOptionWizardPage;
  PaginaManter: TInputOptionWizardPage;
  PaginaSala: TInputFileWizardPage;
  PaginaSenha: TInputQueryWizardPage;
  SalaEscola, SalaNome: String;
  TipoAnterior: String;             { 'professor' ou 'aluno' da instalação anterior; vazio = primeira instalação }
  AtualEscola, AtualNome: String;   { sala já gravada neste PC de aluno (atualização) }
  WndProcAntigo: LongInt;           { procedimento de janela original, para o arrastar-e-soltar }

{ Recebe um arquivo arrastado para a janela: se for .controlesala, preenche o campo da sala. }
procedure TratarArquivoSoltado(Arquivo: String);
begin
  if (PaginaSala <> nil) and (Lowercase(ExtractFileExt(Arquivo)) = '.controlesala') and FileExists(Arquivo) then
  begin
    PaginaSala.Values[0] := Arquivo;
    { Se o PC já tinha sala (atualização), arrastar um pacote significa trocar de sala. }
    if (PaginaManter <> nil) then PaginaManter.SelectedValueIndex := 1;
  end;
end;

{ Novo procedimento de janela do assistente: intercepta o "soltar arquivo" (WM_DROPFILES). }
function WndProcHook(hWnd: HWND; Msg: Cardinal; wParam: LongInt; lParam: LongInt): LongInt;
var
  Buffer: String;
  p: Integer;
begin
  if Msg = WM_DROPFILES then
  begin
    SetLength(Buffer, 1024);
    DragQueryFile(wParam, 0, Buffer, 1024);   { pega o 1º arquivo solto }
    p := Pos(#0, Buffer);
    if p > 0 then Buffer := Copy(Buffer, 1, p - 1);
    DragFinish(wParam);
    TratarArquivoSoltado(Buffer);
    Result := 0;
    Exit;
  end;
  Result := CallWindowProc(WndProcAntigo, hWnd, Msg, wParam, lParam);
end;

{ Liga o arrastar-e-soltar na janela do assistente (uma vez). }
procedure LigarArrastarSoltar;
begin
  if WndProcAntigo <> 0 then Exit;
  DragAcceptFiles(WizardForm.Handle, True);
  { O instalador roda elevado: sem isto o Windows (UIPI) bloqueia arquivos arrastados do Explorer comum. }
  ChangeWindowMessageFilterEx(WizardForm.Handle, WM_DROPFILES, MSGFLT_ALLOW, 0);
  ChangeWindowMessageFilterEx(WizardForm.Handle, WM_COPYGLOBALDATA, MSGFLT_ALLOW, 0);
  WndProcAntigo := SetWindowLong(WizardForm.Handle, GWL_WNDPROC, CreateCallback(@WndProcHook));
end;

{ ---------- tipo de computador, arquivo e senha (assistente ou linha de comando) ---------- }

function EhAluno: Boolean;
begin
  if PaginaTipo <> nil then
    Result := PaginaTipo.SelectedValueIndex = 1
  else
    Result := Lowercase(ExpandConstant('{param:TIPO|}')) = 'aluno';
end;

function Atualizacao: Boolean;
begin
  Result := TipoAnterior <> '';
end;

{ Atualização de PC de aluno que já tem sala: mantém a sala (não pede o pacote de novo), a menos que escolha trocar. }
function MantemSala: Boolean;
begin
  Result := EhAluno and (AtualNome <> '') and (PaginaManter <> nil) and (PaginaManter.SelectedValueIndex = 0);
end;

function EhProfessor: Boolean;
begin
  Result := not EhAluno;
end;

function ArquivoDaSala: String;
begin
  Result := '';
  if PaginaSala <> nil then
    Result := Trim(PaginaSala.Values[0]);
  if Result = '' then
    Result := ExpandConstant('{param:SALA|}');
end;

function SenhaDoPacote: String;
begin
  Result := '';
  if PaginaSenha <> nil then
    Result := PaginaSenha.Values[0];
  if Result = '' then
    Result := ExpandConstant('{param:SENHA|}');
  if Result = '' then
    Result := GetEnv('CONTROLESALA_SENHA');
end;

function PacoteNaPastaDoSetup: String;
var
  Achado: TFindRec;
begin
  Result := '';
  if FindFirst(ExpandConstant('{src}\*.controlesala'), Achado) then
  begin
    try
      Result := ExpandConstant('{src}\') + Achado.Name;
    finally
      FindClose(Achado);
    end;
  end;
end;

function ProgramaTemporario: String;
begin
  Result := ExpandConstant('{tmp}\{#ExeAgente}');
end;

procedure ExtrairAgenteTemporario;
begin
  if not FileExists(ProgramaTemporario) then
    ExtractTemporaryFile('{#ExeAgente}');
end;

{ Lê a sala já gravada neste PC (só existe em PC de aluno instalado como administrador). }
procedure LerSalaAtual;
var
  Codigo: Integer;
  Saida: String;
  Linhas: TArrayOfString;
begin
  AtualEscola := '';
  AtualNome := '';
  if (not IsAdminInstallMode) or (not FileExists(ExpandConstant('{commonappdata}\ControleSala\agente.json'))) then
    Exit;
  ExtrairAgenteTemporario;
  Saida := ExpandConstant('{tmp}\sala-atual.txt');
  DeleteFile(Saida);
  if Exec(ProgramaTemporario, '--sala-atual --saida "' + Saida + '"', '', SW_HIDE, ewWaitUntilTerminated, Codigo) and (Codigo = 0) and
     LoadStringsFromFile(Saida, Linhas) and (GetArrayLength(Linhas) >= 2) then
  begin
    AtualEscola := Linhas[0];
    AtualNome := Linhas[1];
  end;
end;

{ Confere o pacote e a senha usando o próprio agente (a mesma criptografia que o professor usou ao gerar). }
function VerificarPacote(var Erro: String): Boolean;
var
  Codigo: Integer;
  Arquivo, Saida: String;
  Linhas: TArrayOfString;
begin
  Result := False;
  Arquivo := ArquivoDaSala;
  if (Arquivo = '') or (not FileExists(Arquivo)) then
  begin
    Erro := 'Escolha o arquivo da sala (.controlesala) gerado no computador do professor.';
    Exit;
  end;
  if SenhaDoPacote = '' then
  begin
    Erro := 'Digite a senha do pacote da sala.';
    Exit;
  end;

  ExtrairAgenteTemporario;
  Saida := ExpandConstant('{tmp}\sala.txt');
  DeleteFile(Saida);
  SetEnvironmentVariable('CONTROLESALA_SENHA', SenhaDoPacote);
  try
    if not Exec(ProgramaTemporario, '--verificar-sala "' + Arquivo + '" --saida "' + Saida + '"', '', SW_HIDE, ewWaitUntilTerminated, Codigo) then
    begin
      Erro := 'Não foi possível conferir o pacote da sala neste computador.';
      Exit;
    end;
  finally
    SetEnvironmentVariable('CONTROLESALA_SENHA', '');
  end;

  case Codigo of
    0: begin
         if LoadStringsFromFile(Saida, Linhas) and (GetArrayLength(Linhas) >= 2) then
         begin
           SalaEscola := Linhas[0];
           SalaNome := Linhas[1];
         end;
         Result := True;
       end;
    3: Erro := 'Senha incorreta. Confira a senha escolhida ao gerar o pacote.';
    4: Erro := 'Este arquivo não é um pacote de sala do ControleSala (ou está danificado).';
  else
    Erro := 'Não foi possível ler o pacote da sala (código ' + IntToStr(Codigo) + ').';
  end;
end;

{ ---------- assistente ---------- }

{ Instalações antigas (0.1.0) não guardaram o tipo: descobre pelo programa que está na pasta instalada. }
function TipoPelosArquivos: String;
var
  Chave, Pasta: String;
begin
  Result := '';
  Chave := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{CEAD6F19-D73A-4CF4-9684-D09895597A64}_is1';
  if not RegQueryStringValue(HKLM, Chave, 'Inno Setup: App Path', Pasta) then
    if not RegQueryStringValue(HKCU, Chave, 'Inno Setup: App Path', Pasta) then
      Exit;
  if FileExists(AddBackslash(Pasta) + '{#ExeAgente}') then
    Result := 'aluno'
  else if FileExists(AddBackslash(Pasta) + '{#ExeProfessor}') then
    Result := 'professor';
end;

procedure InitializeWizard;
var
  Tipo, Pacote: String;
begin
  TipoAnterior := GetPreviousData('Tipo', '');
  if TipoAnterior = '' then
    TipoAnterior := TipoPelosArquivos;
  Tipo := Lowercase(ExpandConstant('{param:TIPO|}'));
  if Tipo = '' then
    Tipo := TipoAnterior;

  if Atualizacao then
    WizardForm.WelcomeLabel2.Caption := 'Este assistente vai ATUALIZAR o ControleSala deste computador para a versão {#Versao}.' + #13#10#13#10 +
      'A sala, os professores e as configurações continuam como estão.' + #13#10#13#10 +
      'Criado pelo professor {#Autor} · {#Editora}';

  PaginaTipo := CreateInputOptionPage(wpWelcome, 'Tipo de computador',
    'Este computador é do professor ou de um aluno?',
    'Escolha uma opção. O instalador vai instalar só o que este tipo de computador precisa.', True, False);
  PaginaTipo.Add('Computador do PROFESSOR (vê e controla os computadores da sala)');
  PaginaTipo.Add('Computador de ALUNO (o professor poderá ver a tela deste computador)');
  PaginaTipo.Values[0] := Tipo <> 'aluno';
  PaginaTipo.Values[1] := Tipo = 'aluno';

  { Atualização de PC de aluno: oferece manter a sala que já está gravada. }
  LerSalaAtual;
  PaginaManter := CreateInputOptionPage(PaginaTipo.ID, 'Sala deste computador',
    'Este computador já faz parte de uma sala.',
    'Na atualização, o normal é manter a sala. Escolha trocar só se este computador mudou de sala.', True, False);
  PaginaManter.Add('Manter a sala atual: "' + AtualNome + '" (' + AtualEscola + ')');
  PaginaManter.Add('Trocar de sala (escolher outro pacote .controlesala)');
  Pacote := ExpandConstant('{param:SALA|}');
  PaginaManter.Values[0] := Pacote = '';
  PaginaManter.Values[1] := Pacote <> '';

  PaginaSala := CreateInputFilePage(PaginaManter.ID, 'Pacote da sala',
    'Qual é a sala deste computador?',
    'Escolha o arquivo da sala (.controlesala) que o professor gerou no programa dele (botão "Sala...").');
  PaginaSala.Add('Arquivo da sala:', 'Pacote de sala do ControleSala|*.controlesala', '.controlesala');
  if Pacote = '' then
    Pacote := PacoteNaPastaDoSetup;
  PaginaSala.Values[0] := Pacote;

  PaginaSenha := CreateInputQueryPage(PaginaSala.ID, 'Senha do pacote',
    'Digite a senha do pacote da sala',
    'É a senha que o professor escolheu ao gerar o pacote.');
  PaginaSenha.Add('Senha:', True);

  { Arrastar e soltar o .controlesala na janela + uma dica visível na página da sala. }
  LigarArrastarSoltar;
  with TNewStaticText.Create(WizardForm) do
  begin
    Parent := PaginaSala.Surface;
    AutoSize := False;
    WordWrap := True;
    Left := 0;
    Width := PaginaSala.SurfaceWidth;
    Top := ScaleY(74);
    Height := ScaleY(36);
    Caption := 'Dica: você também pode arrastar o arquivo .controlesala (do pendrive) e soltar nesta janela.';
  end;
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  if EhAluno then
    SetPreviousData(PreviousDataKey, 'Tipo', 'aluno')
  else
    SetPreviousData(PreviousDataKey, 'Tipo', 'professor');
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  { na atualização o tipo do computador já é conhecido (para trocar de tipo, desinstale antes) }
  if PageID = PaginaTipo.ID then
    Result := Atualizacao and (ExpandConstant('{param:TIPO|}') = '');
  if PageID = PaginaManter.ID then
    Result := (not EhAluno) or (AtualNome = '');
  if (PageID = PaginaSala.ID) or (PageID = PaginaSenha.ID) then
    Result := (not EhAluno) or MantemSala;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Erro: String;
begin
  Result := True;
  if CurPageID = PaginaSenha.ID then
  begin
    WizardForm.Cursor := crHourGlass;
    try
      Result := VerificarPacote(Erro);
    finally
      WizardForm.Cursor := crDefault;
    end;
    if not Result then
    begin
      if WizardSilent then
      begin
        { Instalação silenciosa: nenhuma janela (ninguém para clicar OK); registra e encerra com código de erro. }
        Log('ERRO: ' + Erro);
        Abort;
      end;
      MsgBox(Erro, mbError, MB_OK);
    end
    else if (SalaNome <> '') and (not WizardSilent) then
      MsgBox('Este computador fará parte da sala "' + SalaNome + '"' + #13#10 + 'da escola ' + SalaEscola + '.', mbInformation, MB_OK);
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
  begin
    if Atualizacao then
      WizardForm.FinishedLabel.Caption := 'O ControleSala foi atualizado para a versão {#Versao}.' + #13#10#13#10 +
        'A sala, os professores e as configurações foram mantidos.'
    else if EhAluno then
      WizardForm.FinishedLabel.Caption := 'O ControleSala foi instalado neste computador de aluno' +
        ' e passa a fazer parte da sala' + #13#10 + SalaNome + '.' + #13#10#13#10 +
        'Ele liga sozinho com o Windows. Perto do relógio aparece o ícone do ControleSala, que mostra' +
        ' quando a tela está sendo vista.'
    else
      WizardForm.FinishedLabel.Caption := 'O ControleSala foi instalado.' + #13#10#13#10 +
        'Abra o programa, entre com o seu nome e clique em "Sala..." para criar a sala' +
        ' e gerar o pacote dos computadores dos alunos.';
  end;
end;

{ ---------- serviço e firewall ---------- }

function Executar(Programa, Parametros: String): Integer;
begin
  if not Exec(Programa, Parametros, '', SW_HIDE, ewWaitUntilTerminated, Result) then
    Result := -1;
end;

function Sc(Parametros: String): Integer;
begin
  Result := Executar(ExpandConstant('{sys}\sc.exe'), Parametros);
end;

function Netsh(Parametros: String): Integer;
begin
  Result := Executar(ExpandConstant('{sys}\netsh.exe'), Parametros);
end;

{ Reserva a porta do agente para o ControleSala. Em alguns PCs o Windows (Hyper-V/virtualização) reserva faixas de
  portas e BLOQUEIA o "bind" do agente (erro de soquete 10013) — aí o agente não liga e o PC não aparece no professor.
  Se a reserva direta falhar (a porta está dentro de uma faixa dinâmica), solta o winnat, reserva e religa.
  Erros são ignorados de propósito (nem todo PC tem winnat, e reservar de novo é inofensivo). }
procedure ReservarPorta;
begin
  if Netsh('int ipv4 add excludedportrange protocol=tcp startport=' + PortaVideo + ' numberofports=1 store=persistent') <> 0 then
  begin
    Executar(ExpandConstant('{sys}\net.exe'), 'stop winnat');
    Netsh('int ipv4 add excludedportrange protocol=tcp startport=' + PortaVideo + ' numberofports=1 store=persistent');
    Executar(ExpandConstant('{sys}\net.exe'), 'start winnat');
  end;
end;

function ServicoExiste: Boolean;
begin
  Result := Sc('query ' + NomeServico) <> 1060; { 1060 = o serviço não existe }
end;

procedure PararAgente;
var
  i: Integer;
begin
  if ServicoExiste then
  begin
    Sc('stop ' + NomeServico);
    for i := 1 to 20 do { espera até 10 s o serviço parar }
    begin
      if Executar(ExpandConstant('{sys}\cmd.exe'), '/c sc query ' + NomeServico + ' | find "STOPPED" >nul') = 0 then
        Break;
      Sleep(500);
    end;
  end;
  Executar(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#ExeAgente}');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Erro: String;
begin
  Result := '';
  if EhAluno and (not MantemSala) and (not VerificarPacote(Erro)) then
  begin
    if WizardSilent then
    begin
      { Instalação silenciosa: nenhuma janela (ninguém para clicar OK); registra e encerra com código de saída de erro. }
      Log('ERRO: ' + Erro);
      Abort;
    end;
    Result := Erro;
    Exit;
  end;
  if IsAdminInstallMode then
    PararAgente; { atualização: solta os arquivos do agente antigo }
end;

procedure ConfigurarAluno;
var
  Exe: String;
  Codigo: Integer;
  SalaGravada: Boolean;
begin
  Exe := ExpandConstant('{app}\{#ExeAgente}');
  if not IsAdminInstallMode then
  begin
    SuppressibleMsgBox('Instalação SEM administrador (só para teste): a sala, o serviço e o firewall NÃO foram configurados.',
      mbInformation, MB_OK, IDOK);
    Exit;
  end;

  { 1) grava a chave da sala numa pasta que só administrador e SYSTEM leem (na atualização, a sala que já estava fica) }
  if MantemSala then
  begin
    SalaNome := AtualNome;
    Codigo := 0;
  end
  else
  begin
    SetEnvironmentVariable('CONTROLESALA_SENHA', SenhaDoPacote);
    try
      Codigo := Executar(Exe, '--importar-sala "' + ArquivoDaSala + '"');
    finally
      SetEnvironmentVariable('CONTROLESALA_SENHA', '');
    end;
  end;
  SalaGravada := Codigo = 0;
  if not SalaGravada then
  begin
    SuppressibleMsgBox('Não foi possível gravar a sala neste computador (código ' + IntToStr(Codigo) + '). O agente NÃO foi ativado.',
      mbError, MB_OK, IDOK);
    Exit;
  end;

  { 2) firewall: só a rede local (sub-rede) pode conectar no agente }
  Netsh('advfirewall firewall delete rule name="' + RegraAgente + '"');
  Netsh('advfirewall firewall add rule name="' + RegraAgente + '" dir=in action=allow program="' + Exe +
        '" protocol=TCP localport=' + PortaVideo + ' remoteip=localsubnet profile=any enable=yes');

  { 2b) garante que a porta do agente não seja bloqueada pelo Windows (erro 10013 em PCs com virtualização) }
  ReservarPorta;

  { 3) serviço do Windows: liga com o PC e reinicia sozinho se cair }
  if ServicoExiste then
    Sc('delete ' + NomeServico);
  Sc('create ' + NomeServico + ' binPath= "\"' + Exe + '\" --servico" start= auto DisplayName= "ControleSala (agente da sala)"');
  Sc('description ' + NomeServico + ' "Permite ao professor da sala ver a tela deste computador. ControleSala, criado pelo professor {#Autor}."');
  Sc('failure ' + NomeServico + ' reset= 86400 actions= restart/5000/restart/5000/restart/30000');
  Sc('start ' + NomeServico);
end;

{ O programa do professor manda esta cópia aos alunos para atualizá-los (Computadores > Atualizar o ControleSala dos alunos). }
procedure GuardarInstaladorParaAlunos;
var
  Pasta, Destino: String;
begin
  Pasta := ExpandConstant('{app}\atualizacao');
  Destino := Pasta + '\ControleSala-Setup.exe';
  if CompareText(ExpandConstant('{srcexe}'), Destino) = 0 then
    Exit; { rodando a própria cópia guardada }
  ForceDirectories(Pasta);
  if not CopyFile(ExpandConstant('{srcexe}'), Destino, False) then
    Log('Não foi possível guardar a cópia do instalador para atualizar os alunos.');
end;

procedure ConfigurarProfessor;
begin
  GuardarInstaladorParaAlunos;
  if not IsAdminInstallMode then Exit;
  { a busca na rede escuta anúncios por UDP: libera só para a rede local }
  Netsh('advfirewall firewall delete rule name="' + RegraProfessor + '"');
  Netsh('advfirewall firewall add rule name="' + RegraProfessor + '" dir=in action=allow program="' +
        ExpandConstant('{app}\{#ExeProfessor}') + '" protocol=UDP localport=' + PortaBusca + ' remoteip=localsubnet profile=any enable=yes');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssInstall) and EhAluno then
    ExtrairAgenteTemporario; { instalação silenciosa não passa pela página da senha }
  if CurStep = ssPostInstall then
  begin
    if EhAluno then ConfigurarAluno else ConfigurarProfessor;
  end;
end;

{ ---------- desinstalação ---------- }

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Pasta: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    if IsAdminInstallMode then
    begin
      { libera internet/sites bloqueados e tira a limpeza agendada, antes de apagar o agente }
      if FileExists(ExpandConstant('{app}\{#ExeAgente}')) then
        Executar(ExpandConstant('{app}\{#ExeAgente}'), '--desfazer');
      PararAgente;
      if ServicoExiste then
        Sc('delete ' + NomeServico);
      Netsh('advfirewall firewall delete rule name="' + RegraAgente + '"');
      Netsh('advfirewall firewall delete rule name="' + RegraProfessor + '"');
      Netsh('int ipv4 delete excludedportrange protocol=tcp startport=' + PortaVideo + ' numberofports=1 store=persistent');
    end;
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    { PC de aluno: apaga a chave da sala e os registros }
    DelTree(ExpandConstant('{commonappdata}\ControleSala'), True, True, True);
    { PC de professor: pergunta antes de apagar professores, sala e chave }
    Pasta := ExpandConstant('{userappdata}\ControleSala');
    if DirExists(Pasta) then
    begin
      if SuppressibleMsgBox('Apagar também os dados do ControleSala deste usuário (professores, sala, chave e lista de computadores)?' + #13#10#13#10 +
           'Se você pretende reinstalar e continuar com a mesma sala, responda NÃO.',
           mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
        DelTree(Pasta, True, True, True);
    end;
  end;
end;
