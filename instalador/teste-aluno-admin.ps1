# ATENÇÃO: não nomeie função de R (é apelido de Invoke-History no PowerShell).
# Teste real do instalador no PC de aluno: instala, confere, e desinstala. PRECISA de administrador (uma única janela UAC).
# Uso (sem elevar): powershell -File instalador\teste-aluno-admin.ps1 -Setup <Setup.exe> -Pacote <x.controlesala> -Senha <s> -Pasta <pasta de trabalho>
param([string]$Setup, [string]$Pacote, [string]$Senha, [string]$Pasta, [switch]$Elevado)

$log = Join-Path $Pasta 'relatorio.txt'
if (-not $Elevado) {
    # relança elevado (UAC) e espera terminar
    Set-Content -Path $log -Value '' -Encoding UTF8
    $args2 = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-Setup', $Setup, '-Pacote', $Pacote, '-Senha', $Senha, '-Pasta', $Pasta, '-Elevado')
    Start-Process powershell.exe -ArgumentList $args2 -Verb RunAs -Wait
    exit
}

function Registrar($texto) { Add-Content -Path $log -Value $texto -Encoding UTF8 }
$pastaApp = Join-Path $env:ProgramFiles 'ControleSala'
$dadosPd = Join-Path $env:ProgramData 'ControleSala'

Registrar '== 1) instalação silenciosa como ALUNO =='
$p = Start-Process $Setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TIPO=aluno', "/SALA=$Pacote", "/SENHA=$Senha" -Wait -PassThru
Registrar "código de saída do Setup: $($p.ExitCode)"
Registrar ('arquivos instalados: ' + ((Get-ChildItem $pastaApp -Name -ErrorAction SilentlyContinue) -join ', '))

Registrar '== 2) serviço =='
$s = Get-Service ControleSalaAgente -ErrorAction SilentlyContinue
Registrar "serviço: $(if ($s) { "$($s.Status), início $($s.StartType)" } else { 'NÃO EXISTE' })"
Start-Sleep -Seconds 8

Registrar '== 3) processo do agente (deve ser SYSTEM, na sessão do usuário) =='
$procs = Get-CimInstance Win32_Process -Filter "Name='ControleSala.Agente.exe'"
if (-not $procs) { Registrar 'NENHUM processo do agente em execução' }
foreach ($x in $procs) {
    $dono = Invoke-CimMethod -InputObject $x -MethodName GetOwner
    Registrar "processo $($x.ProcessId) | sessão $($x.SessionId) | dono $($dono.Domain)\$($dono.User) | $($x.CommandLine)"
}

Registrar '== 4) firewall =='
Registrar ((netsh advfirewall firewall show rule name='ControleSala Agente (video)') -join "`n")

Registrar '== 5) permissões da pasta da sala (só Administradores e SYSTEM) =='
Registrar ((icacls $dadosPd) -join "`n")

Registrar '== 6) agente aceitando conexão e pedindo a chave =='
try {
    $c = New-Object System.Net.Sockets.TcpClient('127.0.0.1', 47810)
    $c.ReceiveTimeout = 4000
    $buf = New-Object byte[] 64
    $n = $c.GetStream().Read($buf, 0, 64)
    Registrar "recebido ao conectar: $n bytes, primeiro byte = $($buf[0]) (esperado 17 bytes, primeiro = 1: exige chave)"
    $c.Close()
} catch { Registrar "falha ao conectar: $($_.Exception.Message)" }

Registrar '== 7) professor (com a chave da sala) conectando =='
$prof = Join-Path $Pasta 'Professor\ControleSala.Professor.exe'
if (Test-Path $prof) {
    $pp = Start-Process $prof -ArgumentList '--dados', (Join-Path $Pasta 'dados'), '--capturar', 'principal', (Join-Path $Pasta 'professor.png'), '--esperar', '8' -Wait -PassThru
    Registrar "captura do professor feita (exit $($pp.ExitCode)): professor.png"
}

Registrar '== 7b) envio de arquivo do professor para o aluno (aparece um aviso perto do relógio) =='
$destino = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'Arquivos do Professor'
if (Test-Path $prof) {
    $origem = Join-Path $Pasta 'origem'
    New-Item -ItemType Directory -Force -Path $origem | Out-Null
    $arq = Join-Path $origem 'Tarefa de teste — ção.txt'
    Set-Content -Path $arq -Value ('Conteúdo de teste do professor.' * 50) -Encoding UTF8
    $res = Join-Path $Pasta 'envio.txt'
    # Start-Process (PowerShell 5) junta a lista sem aspas: caminhos com espaço precisam ir entre aspas.
    $argumentos = '--dados "{0}" --enviar "{1}" --para 127.0.0.1:47810 --resultado "{2}"' -f (Join-Path $Pasta 'dados'), $arq, $res
    $pe = Start-Process $prof -ArgumentList $argumentos -Wait -PassThru
    Registrar "envio (exit $($pe.ExitCode)): $((Get-Content $res -ErrorAction SilentlyContinue) -join ' | ')"
    Registrar ('pasta do aluno: ' + $destino)
    Registrar ((Get-ChildItem $destino -Force -ErrorAction SilentlyContinue | ForEach-Object { "  $($_.Name)  ($($_.Length) bytes)" }) -join "`n")
    Registrar ((icacls $destino) -join "`n")
}

Registrar '== 8) registros =='
foreach ($a in 'servico.log', 'agente.log') {
    $f = Join-Path $dadosPd "logs\$a"
    Registrar "--- $a ---"
    if (Test-Path $f) { Registrar ((Get-Content $f -Tail 14) -join "`n") } else { Registrar '(não existe)' }
}

Registrar '(pausa de 30 s: dá tempo de olhar o ícone do ControleSala perto do relógio, ou na seta ^ de ícones ocultos)'
Start-Sleep -Seconds 30

Registrar '== 9) desinstalação silenciosa =='
$u = Join-Path $pastaApp 'unins000.exe'
if (Test-Path $u) {
    $p2 = Start-Process $u -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait -PassThru
    Registrar "código de saída do desinstalador: $($p2.ExitCode)"
} else { Registrar 'desinstalador não encontrado' }
Start-Sleep -Seconds 3
Registrar "serviço depois: $(if (Get-Service ControleSalaAgente -ErrorAction SilentlyContinue) { 'AINDA EXISTE' } else { 'removido' })"
Registrar "regra de firewall depois: $(if ((netsh advfirewall firewall show rule name='ControleSala Agente (video)') -match 'ControleSala Agente') { 'AINDA EXISTE' } else { 'removida' })"
Registrar "pasta do programa depois: $(if (Test-Path $pastaApp) { 'AINDA EXISTE' } else { 'removida' })"
Registrar "pasta da sala (chave) depois: $(if (Test-Path $dadosPd) { 'AINDA EXISTE' } else { 'removida' })"
Registrar "processo do agente depois: $(if (Get-Process ControleSala.Agente -ErrorAction SilentlyContinue) { 'AINDA RODANDO' } else { 'encerrado' })"
Remove-Item -Recurse -Force $destino -ErrorAction SilentlyContinue   # não deixa a pasta de teste para trás
Registrar "pasta de recebidos removida pelo teste: $(-not (Test-Path $destino))"
Registrar '== fim =='
