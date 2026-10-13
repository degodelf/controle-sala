# Baixa as DLLs do FFmpeg (build LGPL do BtbN) para terceiros\ffmpeg, conferindo a integridade.
# Usado pelo GitHub Actions e serve também localmente. As DLLs NÃO ficam no git (são ~107 MB).
#   powershell -ExecutionPolicy Bypass -File terceiros\baixar-ffmpeg.ps1
$ErrorActionPreference = 'Stop'

$destino = Join-Path $PSScriptRoot 'ffmpeg'
$dlls = 'avcodec-62.dll', 'avutil-60.dll', 'swresample-6.dll', 'swscale-9.dll'

# Se já estão todas lá, não baixa de novo.
if ((Test-Path $destino) -and -not ($dlls | Where-Object { -not (Test-Path (Join-Path $destino $_)) })) {
    Write-Host 'FFmpeg já presente em terceiros\ffmpeg.'
    exit 0
}

$zipNome = 'ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip'
$base = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest'
$baseTmp = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
$tmp = Join-Path $baseTmp ('ffmpeg-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

$zip = Join-Path $tmp $zipNome
Write-Host "Baixando $zipNome ..."
Invoke-WebRequest -Uri "$base/$zipNome" -OutFile $zip
Invoke-WebRequest -Uri "$base/checksums.sha256" -OutFile (Join-Path $tmp 'checksums.sha256')

# Confere o SHA-256 contra o arquivo de checksums da própria release (integridade do download).
$esperado = (Get-Content (Join-Path $tmp 'checksums.sha256') | Where-Object { $_ -match [regex]::Escape($zipNome) }) -split '\s+' | Select-Object -First 1
$obtido = (Get-FileHash $zip -Algorithm SHA256).Hash
if (-not $esperado) { throw "Checksum de $zipNome não encontrado na release." }
if ($esperado -ne $obtido) { throw "SHA-256 não confere.`n esperado: $esperado`n obtido:   $obtido" }
Write-Host "SHA-256 conferido: $obtido"

Expand-Archive -Path $zip -DestinationPath $tmp -Force
$bin = Get-ChildItem -Path $tmp -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'bin') } | Select-Object -First 1
if (-not $bin) { throw 'Pasta bin do FFmpeg não encontrada no zip.' }
$bin = Join-Path $bin.FullName 'bin'

New-Item -ItemType Directory -Force -Path $destino | Out-Null
foreach ($d in $dlls) {
    Copy-Item (Join-Path $bin $d) (Join-Path $destino $d) -Force
}
$licenca = Get-ChildItem -Path (Split-Path $bin -Parent) -Filter 'LICENSE*' -Recurse -File | Select-Object -First 1
if ($licenca) { Copy-Item $licenca.FullName (Join-Path $destino 'LICENSE-FFmpeg-LGPL.txt') -Force }

Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
Write-Host "FFmpeg pronto em $destino"
