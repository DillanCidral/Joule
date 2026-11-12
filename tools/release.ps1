# Gera uma versao do Joule e publica na pasta releases\<canal> (Velopack).
#
#   powershell -File tools\release.ps1 teste      # Joule Teste (violeta), o padrao
#   powershell -File tools\release.ps1 main       # o Joule de uso diario
#   powershell -File tools\release.ps1 main -Versao 1.2.0
#   powershell -File tools\release.ps1 publicar   # manda ao GitHub a ultima versao do main ja gerada
#
# publicar: so com o aval do Dillan na hora (regra 9 do CLAUDE.md). Envia a ultima versao de
# releases\main ao GitHub Releases do repositorio oficial (o atualizador do main busca la),
# publicada e estavel, com o SHA256SUMS junto. O token vem do gh (gh auth login), nunca do disco.
#
# Sem -Versao: a maior ja publicada no canal + 1 no ultimo numero (a primeira e' 0.1.0).
# Enquanto for alpha/beta a versao fica em 0.x; a 1.0.0 e' o lancamento (-Versao 1.0.0).
# Os apps instalados acham a versao nova sozinhos (ao abrir e a cada hora) ou pelo
# menu da bandeja "Procurar atualizacao". Primeira instalacao num PC:
# releases\<canal>\<id>-win-Setup.exe.
param(
    [Parameter(Position = 0)][ValidateSet('main', 'teste', 'publicar')][string]$Canal = 'teste',
    [string]$Versao
)
$ErrorActionPreference = 'Stop'

if ($Canal -eq 'publicar') {
    $root = Split-Path $PSScriptRoot -Parent
    $out  = Join-Path $root 'releases\main'
    $repo = 'https://github.com/DillanCidral/Joule'   # o mesmo do AppInfo.UpdateFeed
    if (-not $Versao) {
        $Versao = Get-ChildItem $out -Filter 'JouleApp-*-full.nupkg' |
            ForEach-Object { if ($_.Name -match '-(\d+\.\d+\.\d+)-') { [version]$Matches[1] } } |
            Sort-Object -Descending | Select-Object -First 1
    }
    if (-not $Versao) { throw "Nenhuma versao do main em $out" }
    $sums = Join-Path $out "SHA256SUMS-$Versao.txt"
    if (-not (Test-Path $sums)) { throw "Falta $sums (gere a versao pelo release.ps1 main)" }
    $token = "$(& gh auth token)".Trim()
    if (-not $token) { throw 'gh sem login (gh auth login)' }
    Write-Host "== Publicar Joule $Versao em $repo ==" -ForegroundColor Cyan
    Push-Location $root   # o vpk e' ferramenta local (.config\dotnet-tools.json)
    try {
        & (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe') tool run vpk -- upload github --outputDir $out --repoUrl $repo `
            --token $token --publish --releaseName "Joule $Versao" --tag "v$Versao"
        if ($LASTEXITCODE) { throw "vpk upload falhou ($LASTEXITCODE)" }
    } finally { Pop-Location }
    & gh release upload "v$Versao" $sums --repo DillanCidral/Joule
    if ($LASTEXITCODE) { throw "gh release upload falhou ($LASTEXITCODE)" }
    Write-Host "Publicado: $repo/releases/tag/v$Versao" -ForegroundColor Green
    return
}

$root   = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'   # o SDK nao esta no PATH
$teste  = $Canal -eq 'teste'
$packId = if ($teste) { 'DillanCidral.JouleTeste' } else { 'JouleApp' }   # sem o nome do dono no download (2026-11-12)
$exe    = if ($teste) { 'JouleTeste.exe' } else { 'Joule.exe' }
$title  = if ($teste) { 'Joule Teste' } else { 'Joule' }
# Tela do instalador: aprovada na pasta identidade-visual (finais\joule\instalador), 2026-11-04.
$splash = Join-Path $root ('tools\instalador\' + $(if ($teste) { 'joule-teste.png' } else { 'joule.png' }))
$barra  = if ($teste) { '#B57BFF' } else { '#FFC21F' }
$out    = Join-Path $root "releases\$Canal"
$pub    = Join-Path $env:TEMP "joule-publish-$Canal"   # temporario, fora do projeto

if (-not $Versao) {
    $last = Get-ChildItem $out -Filter "$packId-*-full.nupkg" -ErrorAction SilentlyContinue |
        ForEach-Object { if ($_.Name -match '-(\d+\.\d+\.\d+)-') { [version]$Matches[1] } } |
        Sort-Object -Descending | Select-Object -First 1
    $Versao = if ($last) { "$($last.Major).$($last.Minor).$($last.Build + 1)" } else { '0.1.0' }
}
Write-Host "== $title $Versao ==" -ForegroundColor Cyan

if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
& $dotnet publish (Join-Path $root 'src\BatteryTray') -c Release -nologo -v q `
    "-p:JouleChannel=$Canal" "-p:Version=$Versao" "-p:JouleTestFeed=$(Join-Path $root 'releases\teste')" -o $pub
if ($LASTEXITCODE) { throw "dotnet publish falhou ($LASTEXITCODE)" }

Push-Location $root   # o vpk e' ferramenta local (.config\dotnet-tools.json)
try {
    & $dotnet tool run vpk -- pack --runtime win-x64 --packId $packId --packVersion $Versao --packDir $pub `
        --mainExe $exe --packTitle $title --packAuthors 'Dillan.apps' `
        --icon (Join-Path $root 'src\BatteryTray\joule.ico') --shortcuts StartMenuRoot --outputDir $out `
        --splashImage $splash --splashProgressColor $barra
    if ($LASTEXITCODE) { throw "vpk pack falhou ($LASTEXITCODE)" }
} finally { Pop-Location; Remove-Item $pub -Recurse -Force -ErrorAction SilentlyContinue }

# Impressao digital (camada de seguranca, 2026-11-12): vai na pagina da versao, para quem baixa conferir que o arquivo nao foi trocado.
$sums = Get-ChildItem $out -File | Where-Object { $_.Name -eq "$packId-win-Setup.exe" -or $_.Name -eq "$packId-$Versao-full.nupkg" } |
    ForEach-Object { "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower())  $($_.Name)" }
$sums | Set-Content (Join-Path $out "SHA256SUMS-$Versao.txt") -Encoding ascii
$sums | ForEach-Object { Write-Host "SHA-256  $_" }
Write-Host "Publicado em $out" -ForegroundColor Green
