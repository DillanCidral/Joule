# Varre uma pasta atras de dado pessoal antes de ela ir ao repositorio publico
# (camada de seguranca, 2026-11-12). Achou algo: lista arquivo:linha e sai com 1.
#
#   powershell -File tools\publico\varrer.ps1 out\repo-publico
#
# Nao guarda nenhum dado do dono: o nome do PC e o usuario vem do Windows na
# hora, e palavras extras (nomes de aparelho, enderecos) de docs\privado\segredos.txt,
# que fica fora do git (uma por linha). Valores ficticios dos testes ficam em
# tools\publico\ficticios.txt.
param([Parameter(Mandatory = $true)][string]$Pasta)
$ErrorActionPreference = 'Stop'

$root  = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$Pasta = (Resolve-Path $Pasta).Path

function Lines($file) {
    if (Test-Path $file) { Get-Content $file -Encoding UTF8 | Where-Object { $_.Trim() -and -not $_.StartsWith('#') } | ForEach-Object { $_.Trim() } } else { @() }
}
$ficticios = @(Lines (Join-Path $PSScriptRoot 'ficticios.txt'))
$segredos  = @(Lines (Join-Path $root 'docs\privado\segredos.txt'))

# Caminho que nunca pode ir, seja qual for o conteudo.
$proibidos = '(^|[\\/])(\.claude|CLAUDE\.md|PRODUCT\.md|capturas|releases|graphify-out|privado|sondas|historico|pesquisas)([\\/]|$)|battery-scan_|joulescan_|\.(exe|zip|nupkg|log)$|[\\/]docs[\\/]design[\\/]'

$regras = @(
    @{ Nome = 'endereco Bluetooth/MAC'; Re = '(?<![0-9A-Fa-f])(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}(?![0-9A-Fa-f])' },
    @{ Nome = 'e-mail';                 Re = '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}' },
    @{ Nome = 'caminho deste PC';       Re = '(?i)[A-Z]:\\Claude code\\|(?:[A-Z]:)?\\Users\\[^\\\s"''\[]+' },
    @{ Nome = 'chave ou token';         Re = '(sk-[A-Za-z0-9]{20}|sk_live_|pk_live_|ghp_[A-Za-z0-9]{20}|github_pat_|AKIA[0-9A-Z]{16}|AIza[0-9A-Za-z_-]{30}|xox[abp]-|-----BEGIN [A-Z ]*PRIVATE KEY)' }
)
foreach ($w in @($env:COMPUTERNAME, $env:USERNAME) + $segredos) {
    if ($w -and $w.Length -ge 4) { $regras += @{ Nome = 'nome deste PC, usuario ou segredo'; Re = '(?i)(?<![A-Za-z0-9])' + [regex]::Escape($w) + '(?![A-Za-z0-9])' } }
}

$binarios = '\.(png|ico|ttf|otf|jpg|jpeg|gif|webp|dll|pdb)$'
$achados = New-Object System.Collections.Generic.List[string]

Get-ChildItem $Pasta -Recurse -File -Force | ForEach-Object {
    $rel = $_.FullName.Substring($Pasta.Length).TrimStart('\', '/')
    if ($rel -match $proibidos) { $achados.Add("$rel : caminho proibido"); return }
    if ($rel -match $binarios) { return }
    $n = 0
    foreach ($linha in [System.IO.File]::ReadAllLines($_.FullName)) {
        $n++
        foreach ($r in $regras) {
            foreach ($m in [regex]::Matches($linha, $r.Re)) {
                if ($ficticios -contains $m.Value) { continue }
                $achados.Add("${rel}:$n : $($r.Nome) ($($m.Value))")
            }
        }
    }
}

if ($achados.Count -gt 0) {
    Write-Host "Varredura: $($achados.Count) achado(s). Nada sai ate resolver:" -ForegroundColor Red
    $achados | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "Varredura: nada pessoal em $Pasta." -ForegroundColor Green
