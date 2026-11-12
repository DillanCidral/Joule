# Monta a pasta do repositorio publico (camada de seguranca, 2026-11-12):
# copia so o que esta em tools\publico\LISTA.txt, poe por cima a documentacao
# de out\publico e roda a varredura. Nao cria repositorio nem envia nada:
# isso e' com o aval do Dillan.
#
#   powershell -ExecutionPolicy Bypass -File tools\publico\montar.ps1
param([string]$Destino = 'out\repo-publico')
$ErrorActionPreference = 'Stop'

$root    = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$destino = [System.IO.Path]::GetFullPath((Join-Path $root $Destino))
$out     = [System.IO.Path]::GetFullPath((Join-Path $root 'out'))
# A pasta e' apagada e refeita: so dentro de out\, nunca em outro lugar.
if (-not $destino.StartsWith($out + '\')) { throw "O destino precisa ficar dentro de $out" }
if (Test-Path $destino) { Remove-Item $destino -Recurse -Force }
New-Item $destino -ItemType Directory | Out-Null

$lista = Get-Content (Join-Path $PSScriptRoot 'LISTA.txt') -Encoding UTF8 | Where-Object { $_.Trim() -and -not $_.StartsWith('#') } | ForEach-Object { $_.Trim() }
foreach ($item in $lista) {
    $origem = Join-Path $root $item
    if (-not (Test-Path $origem)) { throw "Na LISTA mas nao existe: $item" }
    if ($item.EndsWith('/')) {
        Get-ChildItem $origem -Recurse -File -Force |
            Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
            ForEach-Object {
                $rel = $_.FullName.Substring($root.Length + 1)
                $alvo = Join-Path $destino $rel
                New-Item (Split-Path $alvo -Parent) -ItemType Directory -Force | Out-Null
                Copy-Item $_.FullName $alvo
            }
    } else {
        $alvo = Join-Path $destino $item
        New-Item (Split-Path $alvo -Parent) -ItemType Directory -Force | Out-Null
        Copy-Item $origem $alvo
    }
}

Copy-Item (Join-Path $root 'out\publico\*') $destino -Recurse -Force

$arquivos = @(Get-ChildItem $destino -Recurse -File -Force)
Write-Host "Montado em $destino : $($arquivos.Count) arquivos." -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'varrer.ps1') $destino
exit $LASTEXITCODE
