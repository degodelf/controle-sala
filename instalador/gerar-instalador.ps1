# Gera o ControleSala-Setup. Rode na pasta do projeto:  powershell -File instalador\gerar-instalador.ps1
# Requer o .NET 10 SDK e o Inno Setup 6 (ISCC.exe).
$ErrorActionPreference = 'Stop'
# Não deixa o PowerShell transformar o código de saída de um programa externo em erro: tratamos os códigos à mão
# (ex.: o assinar sai com 2 quando não há chave, e isso é um aviso, não uma falha).
$PSNativeCommandUseErrorActionPreference = $false
$raiz = Split-Path -Parent $PSScriptRoot
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:USERPROFILE\ferramentas\InnoSetup\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 não encontrado (ISCC.exe).' }

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1
if (-not $dotnet) { $dotnet = "$env:ProgramFiles\dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { throw '.NET SDK não encontrado.' }

# Publica SEMPRE em pasta limpa: publicar por cima de uma pasta antiga faz o SDK apagar as DLLs laterais (WPF/FFmpeg).
foreach ($p in 'Agente', 'Professor') {
    Remove-Item -Recurse -Force "$raiz\publicar\$p" -ErrorAction SilentlyContinue
    & $dotnet publish "$raiz\src\ControleSala.$p" -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:DebugType=none -o "$raiz\publicar\$p" -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar $p" }
}
# Confere se o Professor publicou as bibliotecas nativas do WPF (sem elas o programa não abre).
foreach ($f in 'ControleSala.Professor.exe', 'PresentationNative_cor3.dll', 'wpfgfx_cor3.dll', 'D3DCompiler_47_cor3.dll', 'PenImc_cor3.dll', 'vcruntime140_cor3.dll') {
    if (-not (Test-Path "$raiz\publicar\Professor\$f")) { throw "Faltou $f na publicação do Professor." }
}
if (-not (Test-Path "$raiz\publicar\Agente\ControleSala.Agente.exe")) { throw 'Faltou o ControleSala.Agente.exe.' }

# A versão do instalador é a mesma dos programas (Directory.Build.props): mude só lá.
$versao = (Select-Xml -Path "$raiz\Directory.Build.props" -XPath '//Version').Node.InnerText
if (-not $versao) { throw 'Versão não encontrada no Directory.Build.props.' }
& $iscc "/DVersao=$versao" "$raiz\instalador\ControleSala.iss"
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o instalador' }

# Assina (para os alunos aceitarem a atualização pela rede). A chave fica só no PC do autor: veja o README, "Atualizar".
& $dotnet run --project "$raiz\src\ControleSala.Assinador" -c Release -- assinar "$raiz\instalador\saida\ControleSala-Setup-$versao.exe" $versao
$assinatura = $LASTEXITCODE
switch ($assinatura) {
    0 { Write-Host 'Instalador assinado.' }
    2 { Write-Warning 'Instalador gerado SEM assinatura (sem chave neste PC): os alunos NAO aceitam atualizacao por ele pela rede.' }
    default { throw "Falha ao assinar o instalador (codigo $assinatura)." }
}
Get-ChildItem "$raiz\instalador\saida\*.exe" | Select-Object Name, @{n='MB'; e={[math]::Round($_.Length / 1MB, 1)}}
# O instalador foi gerado com sucesso (assinar é opcional no servidor): garante que o passo termine OK.
exit 0
