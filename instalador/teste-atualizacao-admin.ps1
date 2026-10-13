# ATENCAO: nao nomeie funcao de R (e apelido de Invoke-History no PowerShell).
# Teste real da ATUALIZACAO no PC de aluno: instala a versao antiga, atualiza para a nova, confere e desinstala.
# PRECISA de administrador (uma unica janela UAC). So rode em PC SEM o ControleSala instalado de verdade.
# Uso (sem elevar): powershell -File instalador\teste-atualizacao-admin.ps1 -SetupAntigo <Setup velho> -SetupNovo <Setup novo> -Pasta <pasta de trabalho>
param([string]$SetupAntigo, [string]$SetupNovo, [string]$Pasta, [switch]$Elevado)

$log = Join-Path $Pasta 'relatorio.txt'
$senha = 'senha-teste-123'
$pacote = Join-Path $Pasta 'teste.controlesala'
$prof = Join-Path (Split-Path -Parent $PSScriptRoot) 'publicar\Professor\ControleSala.Professor.exe'
if (-not $Elevado) {
    New-Item -ItemType Directory -Force -Path $Pasta | Out-Null
    Set-Content -Path $log -Value '' -Encoding UTF8
    # sala de teste numa pasta de dados separada (nao toca na sala real do professor)
    $a = '--dados "{0}" --gerar-pacote "{1}" --escola "Escola Teste" --sala "Sala Teste" --senha {2}' -f (Join-Path $Pasta 'dados'), $pacote, $senha
    Start-Process $prof -ArgumentList $a -Wait
    $args2 = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -SetupAntigo "{1}" -SetupNovo "{2}" -Pasta "{3}" -Elevado' -f $PSCommandPath, $SetupAntigo, $SetupNovo, $Pasta
    Start-Process powershell.exe -ArgumentList $args2 -Verb RunAs -Wait
    exit
}

function Registrar($texto) { Add-Content -Path $log -Value $texto -Encoding UTF8 }
$pastaApp = Join-Path $env:ProgramFiles 'ControleSala'
$agente = Join-Path $pastaApp 'ControleSala.Agente.exe'
$chave = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{CEAD6F19-D73A-4CF4-9684-D09895597A64}_is1'
$regraVideo = 'ControleSala Agente (video)'
$regraBloqueio = 'ControleSala Bloqueio Internet'

function SalaAtual {
    $f = Join-Path $Pasta 'sala-atual.txt'
    Remove-Item $f -ErrorAction SilentlyContinue
    $p = Start-Process $agente -ArgumentList ('--sala-atual --saida "{0}"' -f $f) -Wait -PassThru -WindowStyle Hidden
    "codigo $($p.ExitCode): $((Get-Content $f -ErrorAction SilentlyContinue) -join ' / ')"
}
function Agentes { (Get-CimInstance Win32_Process -Filter "Name='ControleSala.Agente.exe'" | ForEach-Object { "$($_.ProcessId) [$(($_.CommandLine -split ' ')[-1])]" }) -join ', ' }
function Regra($nome) { if ((netsh advfirewall firewall show rule name="$nome") -match [regex]::Escape($nome)) { 'existe' } else { 'nao existe' } }
function Tarefa { schtasks /query /tn ControleSalaLimpeza *> $null; if ($LASTEXITCODE -eq 0) { 'existe' } else { 'nao existe' } }

if ((Test-Path $chave) -or (Get-Service ControleSalaAgente -ErrorAction SilentlyContinue)) {
    Registrar 'ABORTADO: ja existe ControleSala instalado neste PC (nao mexo numa instalacao real).'
    exit
}

Registrar '== 1) instala a versao ANTIGA como aluno =='
$p = Start-Process $SetupAntigo -ArgumentList ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TIPO=aluno /SALA="{0}" /SENHA={1} /LOG="{2}"' -f $pacote, $senha, (Join-Path $Pasta 'log-antigo.txt')) -Wait -PassThru
Registrar "Setup: codigo $($p.ExitCode) | versao do agente: $((Get-Item $agente).VersionInfo.ProductVersion)"
Start-Sleep -Seconds 8
$s = Get-Service ControleSalaAgente -ErrorAction SilentlyContinue
Registrar "servico: $(if ($s) { $s.Status } else { 'NAO EXISTE' }) | processos do agente: $(Agentes)"

Registrar '== 2) simula o que o uso deixa no PC (para a desinstalacao ter o que limpar) =='
# regra DESLIGADA e so para um IP de exemplo: nao corta a internet
netsh advfirewall firewall add rule name="$regraBloqueio" dir=out action=block remoteip=192.0.2.1 enable=no | Out-Null
schtasks /create /tn ControleSalaLimpeza /tr "`"$agente`" --limpar" /sc weekly /d SUN /st 03:00 /ru SYSTEM /f *> $null
Registrar "regra de bloqueio: $(Regra $regraBloqueio) | limpeza agendada: $(Tarefa)"

Registrar '== 3) ATUALIZA para a versao NOVA, silencioso, sem /TIPO, /SALA nem /SENHA =='
$p = Start-Process $SetupNovo -ArgumentList ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="{0}"' -f (Join-Path $Pasta 'log-novo.txt')) -Wait -PassThru
Registrar "Setup: codigo $($p.ExitCode) | versao do agente: $((Get-Item $agente).VersionInfo.ProductVersion)"
$r = Get-ItemProperty $chave
Registrar "registro: versao $($r.DisplayVersion) | tipo guardado: $($r.'Inno Setup CodeFile: Tipo')"
Registrar "programa do professor instalado por engano? $(Test-Path (Join-Path $pastaApp 'ControleSala.Professor.exe'))"
Start-Sleep -Seconds 8
$s = Get-Service ControleSalaAgente -ErrorAction SilentlyContinue
Registrar "servico: $(if ($s) { "$($s.Status), inicio $($s.StartType)" } else { 'NAO EXISTE' }) | processos do agente: $(Agentes)"
Registrar "sala depois da atualizacao: $(SalaAtual)"
Registrar "regra de firewall do agente: $(Regra $regraVideo)"

Registrar '== 4) professor da Sala Teste conecta no agente atualizado =='
$res = Join-Path $Pasta 'professor.png'
$pp = Start-Process $prof -ArgumentList ('--dados "{0}" --capturar principal "{1}" --esperar 8' -f (Join-Path $Pasta 'dados'), $res) -Wait -PassThru
Registrar "captura: codigo $($pp.ExitCode), imagem $(if (Test-Path $res) { "$((Get-Item $res).Length) bytes" } else { 'NAO gerada' })"
$logAgente = Join-Path $env:ProgramData 'ControleSala\logs\agente.log'
if (Test-Path $logAgente) { Registrar ((Get-Content $logAgente -Tail 8) -join "`n") }
Remove-Item $res -ErrorAction SilentlyContinue   # a imagem mostra a tela do usuario: nao guarda

Registrar '== 5) desinstala =='
$p = Start-Process (Join-Path $pastaApp 'unins000.exe') -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -Wait -PassThru
Start-Sleep -Seconds 3
Registrar "desinstalador: codigo $($p.ExitCode)"
Registrar "servico: $(if (Get-Service ControleSalaAgente -ErrorAction SilentlyContinue) { 'AINDA EXISTE' } else { 'removido' })"
Registrar "regra do agente: $(Regra $regraVideo) | regra de bloqueio: $(Regra $regraBloqueio) | limpeza agendada: $(Tarefa)"
Registrar "pasta do programa: $(if (Test-Path $pastaApp) { 'AINDA EXISTE' } else { 'removida' }) | pasta da sala: $(if (Test-Path (Join-Path $env:ProgramData 'ControleSala')) { 'AINDA EXISTE' } else { 'removida' })"
Registrar "processo do agente: $(if (Get-Process ControleSala.Agente -ErrorAction SilentlyContinue) { 'AINDA RODANDO' } else { 'encerrado' })"

# rede de seguranca: o teste nunca deixa nada para tras
if (Get-Service ControleSalaAgente -ErrorAction SilentlyContinue) { sc.exe stop ControleSalaAgente | Out-Null; sc.exe delete ControleSalaAgente | Out-Null }
netsh advfirewall firewall delete rule name="$regraVideo" *> $null
netsh advfirewall firewall delete rule name="$regraBloqueio" *> $null
schtasks /delete /tn ControleSalaLimpeza /f *> $null
Registrar '== fim =='
