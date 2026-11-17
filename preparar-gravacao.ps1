# Prepara o ambiente da gravação e imprime um checklist.
# Uso:  . .\preparar-gravacao.ps1
# O ponto no começo (dot-source) carrega $ip, $token, $tmp e $gameId na sua sessão.
# Sem o ponto, os comandos do roteiro não enxergam as variáveis.

$ErrorActionPreference = 'Continue'
$raiz = $PSScriptRoot
$az = 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd'
$falhas = @()

function Ok($texto, $detalhe) { Write-Host ("  [ok]    {0}  {1}" -f $texto, $detalhe) -ForegroundColor Green }
function Falha($texto, $detalhe) {
    Write-Host ("  [FALHA] {0}  {1}" -f $texto, $detalhe) -ForegroundColor Red
    $script:falhas += $texto
}

Write-Host "`n=== 1. Azure e contexto ===" -ForegroundColor Cyan
$conta = (& $az account show --query user.name -o tsv 2>$null)
if ($conta) { Ok "conta azure" $conta } else { Falha "az login" "rode az login" }

$estado = (& $az aks show -n fcg-aks -g fcg-rg --query powerState.code -o tsv 2>$null)
if ($estado -ne 'Running') {
    Write-Host "  cluster em '$estado'; iniciando (3 a 5 min)..." -ForegroundColor Yellow
    & $az aks start -n fcg-aks -g fcg-rg -o none
    $limite = (Get-Date).AddMinutes(8)
    do {
        Start-Sleep -Seconds 15
        $estado = (& $az aks show -n fcg-aks -g fcg-rg --query powerState.code -o tsv 2>$null)
    } while ($estado -ne 'Running' -and (Get-Date) -lt $limite)
}
if ($estado -eq 'Running') { Ok "cluster" "Running" } else { Falha "cluster" $estado }

# O Docker Desktop troca o contexto dele ao iniciar; é o erro que trava a gravação.
kubectl config use-context fcg-aks *> $null
$ctx = kubectl config current-context 2>$null
if ($ctx -eq 'fcg-aks') { Ok "kubectl" "contexto fcg-aks" } else { Falha "kubectl" "contexto atual: $ctx" }

Write-Host "`n=== 2. Nos e pods ===" -ForegroundColor Cyan
$nos = kubectl get nodes --no-headers 2>$null
if ($nos -match 'aks-nodepool1') { Ok "nos" "$(($nos | Measure-Object).Count) prontos" } else { Falha "nos" "nenhum aks-nodepool1; contexto errado?" }

$pods = kubectl get pods --no-headers 2>$null
$total = ($pods | Measure-Object).Count
$prontos = ($pods | Where-Object { $_ -match '\s1/1\s+Running\s' } | Measure-Object).Count
if ($total -ge 14 -and $prontos -eq $total) { Ok "pods" "$prontos/$total Running" } else { Falha "pods" "$prontos/$total Running" }

Write-Host "`n=== 3. Endereco publico ===" -ForegroundColor Cyan
$ip = (kubectl get svc kong -o jsonpath='{.status.loadBalancer.ingress[0].ip}' 2>$null)
if ($ip) { Ok "kong" "http://${ip}:8000" } else { Falha "kong" "sem EXTERNAL-IP" }

Write-Host "`n=== 4. Login e rotas ===" -ForegroundColor Cyan
$tmp = Join-Path $raiz '.video'
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
Set-Content "$tmp\login.json" -Value '{"email":"cloud9053@fcg.com","senha":"Senha@123"}' -Encoding ascii -NoNewline

$token = $null
if ($ip) {
    $resposta = curl.exe -s -X POST "http://${ip}:8000/api/auth/login" -H "Content-Type: application/json" --data-binary "@$tmp\login.json"
    $token = ($resposta | ConvertFrom-Json).token
}
if ($token) { Ok "login" "token com $($token.Length) caracteres" } else { Falha "login" "sem token; veja o corpo acima" }

$jogos = $null
if ($token) {
    $codigo = curl.exe -s -o NUL -w "%{http_code}" "http://${ip}:8000/api/jogos" -H "Authorization: Bearer $token"
    if ($codigo -eq '200') { Ok "GET /api/jogos" "200 (aquece o cache)" } else { Falha "GET /api/jogos" $codigo }
    $jogos = curl.exe -s "http://${ip}:8000/api/jogos" -H "Authorization: Bearer $token" | ConvertFrom-Json
}

if ($token) {
    $busca = curl.exe -s "http://${ip}:8000/api/jogos/search?q=jgoo" -H "Authorization: Bearer $token"
    if ($busca -match 'Jogo') { Ok "GET /search" "fuzzy achou resultado" } else { Falha "GET /search" "sem resultado" }
}

Write-Host "`n=== 5. Cache e busca ===" -ForegroundColor Cyan
$chave = kubectl exec deploy/redis -- redis-cli --scan --pattern 'catalog:*' 2>$null
if ($chave) {
    $ttl = kubectl exec deploy/redis -- redis-cli TTL catalog:games:all 2>$null
    Ok "redis" "$(($chave | Measure-Object).Count) chave(s), TTL ${ttl}s"
} else { Falha "redis" "sem chave; chame /api/jogos" }

$pf = Start-Process -FilePath 'kubectl' -ArgumentList 'port-forward','svc/elasticsearch','9200:9200' -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 4
$contagem = curl.exe -s "http://localhost:9200/games/_count" 2>$null
Stop-Process -Id $pf.Id -Force -ErrorAction SilentlyContinue
if ($contagem -match '"count"') { Ok "elasticsearch" $contagem } else { Falha "elasticsearch" "sem resposta em :9200" }

Write-Host "`n=== 6. Mongo ===" -ForegroundColor Cyan
$envCfg = @{}
Get-Content (Join-Path $raiz '.fase2-repos\fcg-orchestration\.env') | Where-Object { $_ -match '^([A-Z_]+)=(.*)$' } | ForEach-Object { $envCfg[$matches[1]] = $matches[2] }
$mongoUser = $envCfg['MONGO_USER']
$mongoPass = $envCfg['MONGO_PASSWORD']
$docs = kubectl exec deploy/mongo -- mongosh -u $mongoUser -p $mongoPass --authenticationDatabase admin fcg_catalog --quiet --eval "db.avaliacoes.countDocuments()" 2>$null
if ($docs -match '^\d+$') { Ok "mongo" "$docs avaliacoes na colecao" } else { Falha "mongo" "sem resposta do mongosh" }

$gameId = $null
if ($jogos) { $gameId = $jogos[0].id }

Write-Host "`n=== 7. Gravando as variaveis em .video\ambiente.ps1 ===" -ForegroundColor Cyan
$conteudo = @"
`$ip = '$ip'
`$token = '$token'
`$tmp = '$tmp'
`$gameId = '$gameId'
`$mongoUser = '$mongoUser'
`$mongoPass = '$mongoPass'
`$env:MONGO_USER = '$mongoUser'
`$env:MONGO_PASS = '$mongoPass'
Write-Host "ambiente carregado: ip=`$ip - token de `$(`$token.Length) caracteres" -ForegroundColor Green
"@
[System.IO.File]::WriteAllText("$tmp\ambiente.ps1", $conteudo, (New-Object System.Text.UTF8Encoding($false)))
Ok "ambiente.ps1" "$tmp\ambiente.ps1"

Write-Host ""
if ($falhas.Count -eq 0) {
    Write-Host "PRONTO PARA GRAVAR. Rode agora:  . .\.video\ambiente.ps1" -ForegroundColor Green
    Write-Host "A linha verde 'ambiente carregado' confirma que as variaveis estao na sessao." -ForegroundColor Green
    Write-Host ""
    Write-Host "Lembretes:" -ForegroundColor Yellow
    Write-Host "  - token expira em 8 h; se gravar amanha, rode este script de novo"
    Write-Host "  - cache TTL e 60 s; chame /api/jogos logo antes do bloco 4"
    Write-Host "  - segunda janela para o port-forward do Elasticsearch (bloco 3)"
} else {
    Write-Host ("BLOQUEADO: " + ($falhas -join ', ')) -ForegroundColor Red
}
