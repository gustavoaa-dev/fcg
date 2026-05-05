param(
    [string]$BaseUrl = "http://localhost:5071"
)

function Write-Section {
    param([string]$Title)
    Write-Host ""
    Write-Host "==== $Title ====" -ForegroundColor Cyan
}

function Invoke-ApiJson {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Uri,
        [object]$Body = $null,
        [hashtable]$Headers = @{}
    )

    try {
        if ($null -ne $Body) {
            $jsonBody = $Body | ConvertTo-Json -Depth 10
            $response = Invoke-RestMethod -Method $Method -Uri $Uri -Headers $Headers -ContentType "application/json" -Body $jsonBody
        }
        else {
            $response = Invoke-RestMethod -Method $Method -Uri $Uri -Headers $Headers
        }

        return [pscustomobject]@{
            Success    = $true
            StatusCode = 200
            Data       = $response
            RawBody    = $null
        }
    }
    catch {
        $statusCode = $null
        $rawBody = $null

        if ($_.Exception.Response) {
            $statusCode = [int]$_.Exception.Response.StatusCode
            $rawBody = $_.Exception.Response.Content.ReadAsStringAsync().Result
        }

        return [pscustomobject]@{
            Success    = $false
            StatusCode = $statusCode
            Data       = $null
            RawBody    = $rawBody
        }
    }
}

function Show-Result {
    param(
        [string]$TestName,
        [object]$Result
    )

    if ($Result.Success) {
        Write-Host "[OK] $TestName" -ForegroundColor Green
        if ($null -ne $Result.Data) {
            $Result.Data | ConvertTo-Json -Depth 10
        }
    }
    else {
        Write-Host "[ERRO] $TestName" -ForegroundColor Red
        Write-Host "StatusCode: $($Result.StatusCode)" -ForegroundColor Yellow
        if ($Result.RawBody) {
            Write-Host $Result.RawBody
        }
    }
}

$emailUnico = "usuario.teste.$([guid]::NewGuid().ToString('N').Substring(0,8))@email.com"
$token = $null
$gameId = $null

Write-Host "Base URL: $BaseUrl" -ForegroundColor Yellow

Write-Section "1. Cadastro valido de usuario"
$cadastroValido = Invoke-ApiJson -Method "Post" -Uri "$BaseUrl/api/usuarios" -Body @{
    nome  = "Usuario Teste"
    email = $emailUnico
    senha = "SenhaForte@123"
}
Show-Result -TestName "Cadastro valido" -Result $cadastroValido

Write-Section "2. Cadastro com senha fraca"
$cadastroSenhaFraca = Invoke-ApiJson -Method "Post" -Uri "$BaseUrl/api/usuarios" -Body @{
    nome  = "Usuario Fraco"
    email = "fraco.$([guid]::NewGuid().ToString('N').Substring(0,8))@email.com"
    senha = "123"
}
Show-Result -TestName "Cadastro com senha fraca" -Result $cadastroSenhaFraca

Write-Section "3. Cadastro com email duplicado"
$cadastroDuplicado = Invoke-ApiJson -Method "Post" -Uri "$BaseUrl/api/usuarios" -Body @{
    nome  = "Duplicado"
    email = "gustavo@email.com"
    senha = "SenhaForte@123"
}
Show-Result -TestName "Cadastro com email duplicado" -Result $cadastroDuplicado

Write-Section "4. Login valido e captura de token"
$loginValido = Invoke-ApiJson -Method "Post" -Uri "$BaseUrl/api/auth/login" -Body @{
    email = "gustavo@email.com"
    senha = "Gustavo@123"
}
Show-Result -TestName "Login valido" -Result $loginValido

if ($loginValido.Success -and $loginValido.Data.token) {
    $token = $loginValido.Data.token
    Write-Host "Token capturado com sucesso." -ForegroundColor Green
}
else {
    Write-Host "Token nao capturado. Os testes autenticados podem falhar." -ForegroundColor Yellow
}

Write-Section "5. Login com senha invalida"
$loginInvalido = Invoke-ApiJson -Method "Post" -Uri "$BaseUrl/api/auth/login" -Body @{
    email = "gustavo@email.com"
    senha = "SenhaErrada123"
}
Show-Result -TestName "Login invalido" -Result $loginInvalido

Write-Section "6. Listar jogos com token"
$headers = @{}
if ($token) {
    $headers.Authorization = "Bearer $token"
}
$listarJogosComToken = Invoke-ApiJson -Method "Get" -Uri "$BaseUrl/api/jogos" -Headers $headers
Show-Result -TestName "Listar jogos com token" -Result $listarJogosComToken

Write-Section "7. Listar jogos sem token"
$listarJogosSemToken = Invoke-ApiJson -Method "Get" -Uri "$BaseUrl/api/jogos"
Show-Result -TestName "Listar jogos sem token" -Result $listarJogosSemToken

Write-Section "8. Cadastrar jogo com admin"
$cadastroJogo = Invoke-ApiJson -Method "Post" -Uri "$BaseUrl/api/jogos" -Headers $headers -Body @{
    nome      = "Jogo Terminal"
    descricao = "Criado via script PowerShell"
    preco     = 29.90
}
Show-Result -TestName "Cadastrar jogo com admin" -Result $cadastroJogo

if ($cadastroJogo.Success -and $cadastroJogo.Data.id) {
    $gameId = $cadastroJogo.Data.id
}

Write-Section "9. Cadastrar jogo sem token"
$cadastroJogoSemToken = Invoke-ApiJson -Method "Post" -Uri "$BaseUrl/api/jogos" -Body @{
    nome      = "Jogo Sem Token"
    descricao = "Teste sem autenticacao"
    preco     = 10.00
}
Show-Result -TestName "Cadastrar jogo sem token" -Result $cadastroJogoSemToken

Write-Section "10. Cadastrar jogo invalido"
$cadastroJogoInvalido = Invoke-ApiJson -Method "Post" -Uri "$BaseUrl/api/jogos" -Headers $headers -Body @{
    nome      = "A"
    descricao = ""
    preco     = 0
}
Show-Result -TestName "Cadastrar jogo invalido" -Result $cadastroJogoInvalido

if ($gameId) {
    Write-Section "11. Remover jogo com admin"
    $removerJogo = Invoke-ApiJson -Method "Delete" -Uri "$BaseUrl/api/jogos/$gameId" -Headers $headers
    Show-Result -TestName "Remover jogo com admin" -Result $removerJogo
}
else {
    Write-Section "11. Remover jogo com admin"
    Write-Host "Teste ignorado porque nenhum jogo foi criado com sucesso." -ForegroundColor Yellow
}

Write-Section "Resumo"
Write-Host "Usuario unico usado no teste de cadastro: $emailUnico"
if ($token) {
    Write-Host "Token obtido no login: SIM"
}
else {
    Write-Host "Token obtido no login: NAO"
}
if ($gameId) {
    Write-Host "GameId criado durante o teste: $gameId"
}
