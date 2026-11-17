# Fase 4 — Cloud gerenciado, CI/CD e busca — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** levar a FCG para um cluster Kubernetes gerenciado na nuvem, com imagem em registry privado, pipeline GitHub Actions cobrindo UsersAPI e CatalogAPI, busca por Elasticsearch independente do SQL e nenhuma credencial no código.

**Architecture:** a aplicação continua igual: Kong na frente, cinco serviços .NET 8, RabbitMQ entre eles, SQL Server e MongoDB como persistências, Redis como cache. A fase acrescenta três peças — cluster AKS + ACR (infra), GitHub Actions por serviço (entrega) e Elasticsearch com índice alimentado pelo CatalogAPI (busca). Mongo e Redis já existem e são reaproveitados, não reimplementados.

**Tech Stack:** AKS + ACR (Azure), GitHub Actions, Docker, kubectl, Elasticsearch 8 (single node), `HttpClient` + `System.Text.Json` para o índice, xUnit + Moq para os testes novos.

**Spec:** enunciado da Fase 4 (texto do usuário, 2026-11-08). As seções citadas abaixo vêm dele; não há arquivo de spec no repo.

## Global Constraints

- Cluster Kubernetes **gerenciado** (AKS, EKS, GKE ou OKE). Instância com containers na mão não vale.
- Registry **privado e nativo** da cloud escolhida (ACR para Azure).
- Aplicação exposta externamente por LoadBalancer ou Ingress.
- MongoDB **ou** DynamoDB obrigatório, com driver oficial.
- Redis obrigatório (`StackExchange.Redis` ou `IDistributedCache`).
- Elasticsearch/OpenSearch obrigatório, motor de busca **independente** da consulta relacional.
- `/search` com **fuzzy search** e ordenação por **relevância**.
- Índice sincronizado a cada jogo **inserido ou editado**.
- Pipeline cobre **UsersAPI e CatalogAPI**; `main` reflete produção.
- Build & Test, imagem com tag, push no registry, deploy com **rolling update** (sem downtime).
- **Zero hardcoded credentials** em código e em YAML.

## O que já existe (não refazer)

| Requisito | Situação | Evidência |
|---|---|---|
| MongoDB | Feito — avaliações em coleção `reviews`, driver oficial | `fcg-catalog-api\FCG.CatalogAPI.Infrastructure\Repositories\MongoReviewRepository.cs`, `Program.cs:L129-L137` |
| Redis | Feito — cache-aside do catálogo, degrada para SQL | `Repositories\CachedGameRepository.cs`, `Program.cs:L105-L118` |
| KEDA/serverless | Feito na fase 3 | `fcg-notifications-function`, `k8s` do orchestration |
| Deploy em k8s | Existe, mas em cluster local (docker-desktop) | `k8s\*.yaml`, `imagePullPolicy: IfNotPresent`, tags `sp3-*` |
| Exposição | Kong `LoadBalancer` na porta 8000 | `k8s\kong\kong-deployment.yaml:L83` |

**Lacunas reais:** cluster na nuvem, registry, pipeline, Elasticsearch, testes que a pipeline possa rodar, credenciais hardcoded.

---

### Task 1: Cluster gerenciado e registry

**Files:**
- Create: `fcg-orchestration\infra\aks-up.ps1`
- Create: `fcg-orchestration\infra\README.md`

**Interfaces:**
- Produces: cluster `fcg-aks` no grupo `fcg-rg`, registry `fcgacr2026`, contexto kubectl apontando para ele; `infra\acr.txt` com o login server, consumido pelas tasks 2 e 4.

- [ ] **Step 1: Confirmar se a fase anterior já deixou cluster/registry**

```powershell
az account show --query name -o tsv
az aks list --query "[].{nome:name, rg:resourceGroup, versao:currentKubernetesVersion}" -o table
az acr list --query "[].{nome:name, loginServer:loginServer}" -o table
```

Se já existir cluster da fase anterior, anote nome e RG, pule para o Step 5 e registre no README. Se não existir, siga.

- [ ] **Step 2: Registrar os provedores de recurso**

Assinatura nova nasce sem eles; a criação falha com `MissingSubscriptionRegistration`.

```powershell
foreach ($ns in 'Microsoft.ContainerRegistry','Microsoft.ContainerService','Microsoft.Kubernetes',
                'Microsoft.KubernetesConfiguration','Microsoft.Compute','Microsoft.Network',
                'Microsoft.Storage','Microsoft.ManagedIdentity') {
    az provider register --namespace $ns --wait -o none
}
```

- [ ] **Step 3: Criar grupo, registry e cluster**

```powershell
$regiao = 'eastus'          # brazilsouth custa 38% mais no compute
az group create -n fcg-rg -l $regiao
az acr create -n fcgacr2026 -g fcg-rg --sku Basic --admin-enabled false
az aks create -n fcg-aks -g fcg-rg `
  --node-count 2 --node-vm-size Standard_D2s_v4 `
  --tier free `
  --enable-managed-identity --attach-acr fcgacr2026
```

`Standard_D2s_v4` = 2 vCPU / 8 GiB por nó, R$ 0,5161/h em eastus. A família B **não é permitida** nesta assinatura: `az aks create` falha com `BadRequest` e lista os tamanhos aceitos (só família D). A quota padrão é 4 vCPU por família e 4 no total, então dois nós `D2s_v4` ocupam a cota inteira — pedir aumento antes de tentar um terceiro nó. Dois nós (16 GiB) cabem a stack: os requests somam cerca de 6 GiB. `--tier free` evita o control plane de R$ 0,5376/h do tier Standard. Se algum pod ficar `Pending` por memória, o primeiro a sair é o Loki (`fcg-orchestration\k8s\loki-deployment.yaml`).

- [ ] **Step 3: Apontar kubectl para o cluster e guardar o nome do registry**

```powershell
az aks get-credentials -n fcg-aks -g fcg-rg --overwrite-existing
az acr list -g fcg-rg --query "[0].loginServer" -o tsv | Set-Content .\infra\acr.txt
```

- [ ] **Step 4: Verificar**

```powershell
kubectl get nodes
kubectl get storageclass
```

Esperado: 3 nós `Ready`; classe `managed-csi` marcada como `(default)`.

- [ ] **Step 5: Commit**

```powershell
git add infra/aks-up.ps1 infra/README.md
git commit -m "feat(infra): script de provisionamento do AKS e ACR"
```

---

### Task 2: Primeiro deploy na nuvem (manual)

**Files:**
- Modify: `fcg-orchestration\k8s\mongo-deployment.yaml:L10`, `prometheus-deployment.yaml:L10`, `rabbitmq-deployment.yaml:L10`, `sqlserver-deployment.yaml:L10` (remover `storageClassName: standard`)
- Modify: `fcg-orchestration\k8s\sqlserver-deployment.yaml:L13` (`storage: 1Gi` → `5Gi`)
- Modify: `fcg-orchestration\k8s\*.yaml` — campo `image:` de cada serviço para `<acr>/fcg-<servico>:1.0.0`
- Create: `fcg-orchestration\k8s\README-aks.md` — uma nota dizendo que o ACR já está anexado ao cluster, então não há `imagePullSecret`

**Interfaces:**
- Consumes: cluster e ACR da Task 1.
- Produces: aplicação respondendo no IP externo do Kong; imagens `1.0.0` das cinco APIs no ACR.

- [ ] **Step 1: Remover a classe de storage fixa**

`standard` não existe no AKS; os PVCs ficariam `Pending` e Mongo, SQL Server, RabbitMQ e Prometheus não subiriam. Apagar a linha `storageClassName: standard` faz o PVC usar a classe default do cluster.

```yaml
# antes
  storageClassName: standard
  resources:
    requests:
      storage: 1Gi
# depois
  resources:
    requests:
      storage: 1Gi
```

- [ ] **Step 2: Subir o SQL Server com volume maior**

O volume de 1 Gi é pequeno para banco mais logs do SQL Server. Trocar para `5Gi` no mesmo PVC do Step 1.

- [ ] **Step 3: Construir as imagens no ACR, sem Docker local**

```powershell
$acr = Get-Content .\infra\acr.txt
az acr build -r $acr -t fcg-users-api:1.0.0 ..\fcg-users-api
az acr build -r $acr -t fcg-catalog-api:1.0.0 ..\fcg-catalog-api
az acr build -r $acr -t fcg-payments-api:1.0.0 ..\fcg-payments-api
az acr build -r $acr -t fcg-notifications-api:1.0.0 ..\fcg-notifications-api
```

`az acr build` compila no serviço, então não depende de Docker na máquina nem de `docker push`.

- [ ] **Step 4: Ajustar as tags e aplicar**

```powershell
$acr = Get-Content .\infra\acr.txt
(Get-ChildItem .\k8s\*.yaml) | ForEach-Object {
  (Get-Content $_.FullName) -replace 'image: fcg-', "image: $acr/fcg-" | Set-Content $_.FullName
}
kubectl apply -f .\k8s\
kubectl get pods -w
```

Esperado: todos os pods `Running`; `kubectl get svc kong` mostra um `EXTERNAL-IP` público.

- [ ] **Step 5: Verificar o fluxo real pelo IP externo**

```powershell
$ip = kubectl get svc kong -o jsonpath='{.status.loadBalancer.ingress[0].ip}'
curl.exe -s -X POST "http://$ip`:8000/api/usuarios" -H "Content-Type: application/json" -d '{\"nome\":\"Teste Cloud\",\"email\":\"cloud1@fcg.com\",\"senha\":\"Senha@123\"}'
curl.exe -s -X POST "http://$ip`:8000/api/auth/login" -H "Content-Type: application/json" -d '{\"email\":\"cloud1@fcg.com\",\"senha\":\"Senha@123\"}'
```

Esperado: `201` no cadastro, `200` com token no login.

- [ ] **Step 6: Commit**

```powershell
git add k8s/
git commit -m "feat(k8s): deploy no AKS com imagens do ACR e storage default"
```

---

### Task 3: Testes mínimos em UsersAPI e CatalogAPI

**Files:**
- Create: `.fase2-repos\fcg-users-api\FCG.UsersAPI.Tests\FCG.UsersAPI.Tests.csproj`
- Create: `.fase2-repos\fcg-users-api\FCG.UsersAPI.Tests\UserServiceTests.cs`
- Create: `.fase2-repos\fcg-users-api\FCG.UsersAPI.Tests\AuthServiceTests.cs`
- Create: `.fase2-repos\fcg-catalog-api\FCG.CatalogAPI.Tests\FCG.CatalogAPI.Tests.csproj`
- Create: `.fase2-repos\fcg-catalog-api\FCG.CatalogAPI.Tests\GameServiceTests.cs`
- Modify: `fcg-users-api\FCG.UsersAPI.sln`, `fcg-catalog-api\FCG.CatalogAPI.sln` (incluir o projeto de teste)

**Interfaces:**
- Consumes: `UserService.CriarUsuario`, `AuthService.GerarToken`, `GameService.IniciarCompra`.
- Produces: `dotnet test` verde nos dois repos; é o comando que a pipeline da Task 4 chama.

Sem teste, o stage `Build & Test` da pipeline passa sem verificar nada. Quatro testes cobrem o que move dinheiro e identidade.

- [ ] **Step 1: Criar o projeto de teste do UsersAPI**

```powershell
cd .fase2-repos\fcg-users-api
dotnet new xunit -n FCG.UsersAPI.Tests
dotnet add FCG.UsersAPI.Tests reference FCG.UsersAPI.Application FCG.UsersAPI.Domain
dotnet add FCG.UsersAPI.Tests package Moq
dotnet sln add FCG.UsersAPI.Tests
```

- [ ] **Step 2: Teste de cadastro duplicado**

```csharp
using FCG.Shared.Events;
using FCG.UsersAPI.Application.DTOs;
using FCG.UsersAPI.Application.Services;
using FCG.UsersAPI.Domain.Entities;
using FCG.UsersAPI.Domain.Enums;
using FCG.UsersAPI.Domain.Interfaces;
using MassTransit;
using Moq;

public class UserServiceTests
{
    [Fact]
    public async Task CriarUsuario_EmailJaCadastrado_NaoDevePublicarEvento()
    {
        var existente = new User("Dono", "dup@fcg.com", "hash", UserRole.Usuario);
        var repo = new Mock<IUserRepository>();
        repo.Setup(r => r.ObterPorEmail("dup@fcg.com")).ReturnsAsync(existente);
        var publish = new Mock<IPublishEndpoint>();
        var service = new UserService(repo.Object, publish.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CriarUsuario(new CriarUsuarioDTO { Nome = "Novo", Email = "dup@fcg.com", Senha = "Senha@123" }));

        publish.Verify(p => p.Publish(It.IsAny<UserCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 3: Rodar**

```powershell
dotnet test FCG.UsersAPI.Tests
```

Esperado: `Passed: 1`. Se falhar por `UserCreatedEvent` não encontrado, acrescentar `using FCG.Shared.Events;` (o pacote compartilhado já é referenciado pelo projeto Application).

- [ ] **Step 4: Teste de login com senha errada**

```csharp
[Fact]
public async Task GerarToken_SenhaIncorreta_DeveLancarUnauthorized()
{
    var repo = new Mock<IUserRepository>();
    repo.Setup(r => r.ObterPorEmail("u@fcg.com"))
        .ReturnsAsync(new User("U", "u@fcg.com", BCrypt.Net.BCrypt.HashPassword("Certa@123"), UserRole.Usuario));
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Jwt:SecretKey"] = "chave-de-teste-com-32-caracteres!!",
        ["Jwt:Issuer"] = "FCG.UsersAPI",
        ["Jwt:Audience"] = "FCG.Client",
        ["Jwt:ExpiracaoHoras"] = "8"
    }).Build();

    await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
        new AuthService(repo.Object, config).GerarToken(new LoginDTO { Email = "u@fcg.com", Senha = "Errada@123" }));
}
```

Esse teste passa no código atual: `AuthService.cs:L32-L33` já lança `UnauthorizedAccessException` para senha errada. O caso de e-mail inexistente tem bug (Task 6).

- [ ] **Step 5: Criar o projeto de teste do CatalogAPI**

```powershell
cd ..\fcg-catalog-api
dotnet new xunit -n FCG.CatalogAPI.Tests
dotnet add FCG.CatalogAPI.Tests reference FCG.CatalogAPI.Application FCG.CatalogAPI.Domain
dotnet add FCG.CatalogAPI.Tests package Moq
dotnet sln add FCG.CatalogAPI.Tests
```

```csharp
[Fact]
public async Task IniciarCompra_JogoJaNaBiblioteca_DeveLancarArgumentException()
{
    var gameId = Guid.NewGuid();
    var games = new Mock<IGameRepository>();
    games.Setup(g => g.ObterPorId(gameId)).ReturnsAsync(new Game("Jogo", "Desc", 10m));
    var userGames = new Mock<IUserGameRepository>();
    userGames.Setup(u => u.ObterPorIds(It.IsAny<Guid>(), gameId))
        .ReturnsAsync(new UserGame(Guid.NewGuid(), gameId));

    var service = new GameService(games.Object, userGames.Object, Mock.Of<IPublishEndpoint>());

    await Assert.ThrowsAsync<ArgumentException>(() =>
        service.IniciarCompra(new IniciarCompraDTO { UserId = Guid.NewGuid(), GameId = gameId }));
}
```

- [ ] **Step 6: Rodar e commitar**

```powershell
dotnet test
git add -A
git commit -m "test: testes mínimos de cadastro, login e compra"
```

Esperado: `Failed: 0` nos dois repos.

---

### Task 4: Pipeline CI/CD (UsersAPI e CatalogAPI)

**Files:**
- Create: `.fase2-repos\fcg-users-api\.github\workflows\ci-cd.yml`
- Create: `.fase2-repos\fcg-catalog-api\.github\workflows\ci-cd.yml`
- Create: `fcg-orchestration\infra\README.md` (seção "Segredos do GitHub")

**Interfaces:**
- Consumes: `dotnet test` da Task 3; ACR e cluster da Task 1; deployments da Task 2.
- Produces: push em `main` gera imagem `:<sha>` e rolling update; workflow `ci-cd` verde.

- [ ] **Step 1: Criar o service principal e gravar os segredos**

```powershell
az ad sp create-for-rbac --name fcg-github --role contributor `
  --scopes /subscriptions/<SUBSCRIPTION_ID>/resourceGroups/fcg-rg `
  --sdk-auth
```

Gravar como secret `AZURE_CREDENTIALS` no repo. Gravar também `KUBE_CONFIG` (conteúdo do kubeconfig em base64).

- [ ] **Step 2: Escrever o workflow**

```yaml
name: ci-cd
on:
  push:
    branches: [main]
  workflow_dispatch:

env:
  SERVICO: fcg-users-api
  DEPLOYMENT: users-api

jobs:
  build-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.0.x' }
      - run: dotnet restore
      - run: dotnet build --no-restore -c Release
      - run: dotnet test --no-build -c Release --logger "console;verbosity=normal"

  image-deploy:
    needs: build-test
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: azure/login@v2
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }}
      - name: Build e push da imagem no ACR
        run: |
          acr=$(az acr list -g fcg-rg --query "[0].loginServer" -o tsv)
          az acr build -r "$acr" -t "$SERVICO:${{ github.sha }}" .
      - name: Rolling update
        run: |
          echo "${{ secrets.KUBE_CONFIG }}" | base64 -d > kubeconfig
          export KUBECONFIG=$PWD/kubeconfig
          acr=$(az acr list -g fcg-rg --query "[0].loginServer" -o tsv)
          kubectl -n default set image deployment/$DEPLOYMENT \
            $DEPLOYMENT="$acr/$SERVICO:${{ github.sha }}"
          kubectl -n default rollout status deployment/$DEPLOYMENT --timeout=180s
```

No repo do CatalogAPI, trocar `SERVICO: fcg-catalog-api` e `DEPLOYMENT: catalog-api`.

- [ ] **Step 3: Verificar o rolling update**

```powershell
git commit -am "ci: pipeline de build, teste e deploy" ; git push
gh run watch
kubectl get pods -l app=catalog-api
```

Esperado: workflow verde; pods novos com a tag `<sha>`; `kubectl describe deployment catalog-api` mostra o rollout sem indisponibilidade.

- [ ] **Step 4: Provar o restart sem downtime**

```powershell
1..20 | ForEach-Object { curl.exe -s -o NUL -w "%{http_code}`n" "http://<ip>:8000/api/jogos" }
```

Esperado: 20 códigos, nenhum `502`.

---

### Task 5: Elasticsearch, índice e `/search` com fuzzy

**Files:**
- Create: `fcg-catalog-api\FCG.CatalogAPI.Domain\Interfaces\IGameSearchIndex.cs`
- Create: `fcg-catalog-api\FCG.CatalogAPI.Infrastructure\Search\ElasticGameSearchIndex.cs`
- Modify: `fcg-catalog-api\FCG.CatalogAPI.Application\Services\GameService.cs`
- Modify: `fcg-catalog-api\FCG.CatalogAPI.API\Controllers\GamesController.cs`
- Modify: `fcg-catalog-api\FCG.CatalogAPI.API\Program.cs`
- Modify: `fcg-catalog-api\FCG.CatalogAPI.API\appsettings.json` (seção `Elastic:Uri`)
- Create: `fcg-orchestration\k8s\elasticsearch-deployment.yaml`
- Test: `fcg-catalog-api\FCG.CatalogAPI.Tests\ElasticGameSearchIndexTests.cs`

**Interfaces:**
- Produces: `IGameSearchIndex.IndexarAsync(Game)`, `RemoverAsync(Guid)`, `BuscarAsync(string) → IReadOnlyList<Game>`, `ReindexarAsync(IEnumerable<Game>)`; `GameService.Buscar(string) → IReadOnlyList<GameResponseDTO>`; endpoint `GET /api/jogos/search?q=<termo>`.
- Consumes: `IGameRepository.ObterTodos()` no reindex de boot.

O contrato fica no Domain, junto dos repositórios existentes: Infrastructure e Application não se referenciam entre si, mas as duas referenciam Domain. `Game` já tem o construtor de reidratação `(id, nome, descricao, preco, dataCadastro)` (`Domain\Entities\Game.cs:L27-L35`), então a busca devolve `Game` com o Id original — nenhum DTO novo no caminho.

- [ ] **Step 1: Escrever o teste do índice com handler falso**

```csharp
using FCG.CatalogAPI.Infrastructure.Search;

public class ElasticGameSearchIndexTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public string? Corpo;
        public Uri? Uri;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
        {
            Uri = r.RequestUri;
            Corpo = r.Content is null ? null : await r.Content.ReadAsStringAsync(c);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent("{\"hits\":{\"hits\":[]}}") };
        }
    }

    [Fact]
    public async Task BuscarAsync_DeveEnviarFuzzyNoNome()
    {
        var handler = new FakeHandler();
        var sut = new ElasticGameSearchIndex(new HttpClient(handler) { BaseAddress = new Uri("http://es:9200") });

        await sut.BuscarAsync("jgoo");

        Assert.Contains("\"fuzziness\":\"AUTO\"", handler.Corpo);
        Assert.Contains("nome^3", handler.Corpo);
        Assert.Contains("_search", handler.Uri!.ToString());
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

```powershell
cd .fase2-repos\fcg-catalog-api
dotnet test --filter BuscarAsync_DeveEnviarFuzzyNoNome
```

Esperado: FAIL de compilação, `ElasticGameSearchIndex` não existe.

- [ ] **Step 3: Contrato no Domain**

```csharp
using FCG.CatalogAPI.Domain.Entities;

namespace FCG.CatalogAPI.Domain.Interfaces;

public interface IGameSearchIndex
{
    Task IndexarAsync(Game game);
    Task RemoverAsync(Guid id);
    Task<IReadOnlyList<Game>> BuscarAsync(string termo);
    Task ReindexarAsync(IEnumerable<Game> games);
}
```

- [ ] **Step 4: Implementar com `HttpClient`**

Sem pacote de cliente: são três chamadas HTTP e `System.Text.Json` já está no projeto. Um cliente oficial traria versionamento de API do Elasticsearch sem benefício aqui.

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using FCG.CatalogAPI.Domain.Entities;
using FCG.CatalogAPI.Domain.Interfaces;

namespace FCG.CatalogAPI.Infrastructure.Search;

public class ElasticGameSearchIndex : IGameSearchIndex
{
    private const string Indice = "games";
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public ElasticGameSearchIndex(HttpClient http) => _http = http;

    public async Task IndexarAsync(Game game) =>
        await _http.PutAsJsonAsync($"/{Indice}/_doc/{game.Id}",
            new { nome = game.Nome, descricao = game.Descricao, preco = game.Preco, dataCadastro = game.DataCadastro },
            _json);

    public async Task RemoverAsync(Guid id) => await _http.DeleteAsync($"/{Indice}/_doc/{id}");

    public async Task ReindexarAsync(IEnumerable<Game> games)
    {
        foreach (var game in games) await IndexarAsync(game);
    }

    public async Task<IReadOnlyList<Game>> BuscarAsync(string termo)
    {
        var corpo = new
        {
            size = 20,
            query = new
            {
                multi_match = new { query = termo, fields = new[] { "nome^3", "descricao" }, fuzziness = "AUTO" }
            }
        };

        var resposta = await _http.PostAsJsonAsync($"/{Indice}/_search", corpo, _json);
        resposta.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("hits").GetProperty("hits").EnumerateArray()
            .Select(h =>
            {
                var fonte = h.GetProperty("_source");
                return new Game(
                    Guid.Parse(h.GetProperty("_id").GetString()!),
                    fonte.GetProperty("nome").GetString()!,
                    fonte.GetProperty("descricao").GetString()!,
                    fonte.GetProperty("preco").GetDecimal(),
                    fonte.GetProperty("dataCadastro").GetDateTime());
            })
            .ToList();
    }
}
```

O `_id` do documento é o `Game.Id`; a origem não carrega o id, por isso ele vem do hit.

- [ ] **Step 5: Mapear no `GameService` e expor a busca**

O construtor passa a receber `IGameSearchIndex search` e `ILogger<GameService> logger`, guardados em `_search` e `_logger`. O mapeamento reaproveita o `MapearParaGameResponse` que já existe na classe.

```csharp
public async Task<IReadOnlyList<GameResponseDTO>> Buscar(string termo) =>
    (await _search.BuscarAsync(termo)).Select(MapearParaGameResponse).ToList();
```

O construtor de `GameService` muda, então a construção dentro de `GameServiceTests` (Task 3) precisa dos dois parâmetros novos — `Mock.Of<IGameSearchIndex>()` e `NullLogger<GameService>.Instance` — senão o projeto de teste deixa de compilar.

- [ ] **Step 6: Registrar no DI e popular no boot**

```csharp
var elasticUri = builder.Configuration["Elastic:Uri"] ?? "http://elasticsearch:9200";
builder.Services.AddHttpClient<IGameSearchIndex, ElasticGameSearchIndex>(
    c => c.BaseAddress = new Uri(elasticUri));
```

No `Program.cs`, depois do `Migrate()`:

```csharp
var search = scope.ServiceProvider.GetRequiredService<IGameSearchIndex>();
var jogos = await scope.ServiceProvider.GetRequiredService<IGameRepository>().ObterTodos();
await search.ReindexarAsync(jogos);
```

Com dezenas de jogos, reindexar sempre no boot custa menos que detectar se o índice está vazio. Envolver em `try/catch` com `LogWarning`, como o Mongo em `Program.cs:L153-L162`.

- [ ] **Step 7: Indexar na escrita**

Em `GameService`, usar o `_search` e o `_logger` que o construtor recebeu no Step 5, chamando depois do `Salvar()`:

```csharp
await _gameRepository.Salvar();
await IndexarSeguro(game);          // CriarGame e Atualizar
await RemoverDoIndiceSeguro(id);    // Remover

private async Task IndexarSeguro(Game game)
{
    try { await _search.IndexarAsync(game); }
    catch (Exception ex) { _logger.LogWarning(ex, "Falha ao indexar o jogo {GameId} no Elasticsearch.", game.Id); }
}

private async Task RemoverDoIndiceSeguro(Guid id)
{
    try { await _search.RemoverAsync(id); }
    catch (Exception ex) { _logger.LogWarning(ex, "Falha ao remover o jogo {GameId} do Elasticsearch.", id); }
}
```

Falha do Elasticsearch não derruba o cadastro: o índice é reconstruível, o dado no SQL não.

- [ ] **Step 8: `PUT /api/jogos/{id}` para a metade "editado"**

O requisito fala em "inserido ou editado" e hoje não existe edição de jogo.

```csharp
[HttpPut("{id:guid}")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> Atualizar(Guid id, [FromBody] CriarGameDTO dto)
{
    await _gameService.Atualizar(id, dto);
    return NoContent();
}
```

`GameService.Atualizar`: `ObterPorId` (404 se não existir), atribuir nome/descrição/preço, `Salvar()` e `IndexarSeguro(game)`. O `CachedGameRepository.Salvar()` já invalida as chaves do Redis.

O `Game` tem setters privados; acrescentar um único método `Alterar(string nome, string descricao, decimal preco)` que atribui os três campos. Um método, não três.

- [ ] **Step 9: Endpoint de busca**

```csharp
[HttpGet("search")]
[Authorize]
public async Task<ActionResult<IEnumerable<GameResponseDTO>>> Buscar([FromQuery] string q)
{
    if (string.IsNullOrWhiteSpace(q)) return BadRequest(new { mensagem = "Informe o termo de busca." });
    return Ok(await _gameService.Buscar(q));
}
```

A rota `search` não conflita com `{id:guid}`, porque "search" não é GUID.

- [ ] **Step 10: Manifesto do Elasticsearch**

```yaml
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: elasticsearch-data
spec:
  accessModes: [ReadWriteOnce]
  resources:
    requests:
      storage: 2Gi
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: elasticsearch
  labels: { app: elasticsearch }
spec:
  replicas: 1
  selector:
    matchLabels: { app: elasticsearch }
  template:
    metadata:
      labels: { app: elasticsearch }
    spec:
      containers:
        - name: elasticsearch
          image: docker.elastic.co/elasticsearch/elasticsearch:8.15.3
          env:
            - name: discovery.type
              value: single-node
            - name: xpack.security.enabled
              value: "false"
            - name: ES_JAVA_OPTS
              value: -Xms512m -Xmx512m
          ports:
            - containerPort: 9200
          resources:
            limits: { memory: 1Gi }
          volumeMounts:
            - name: data
              mountPath: /usr/share/elasticsearch/data
      volumes:
        - name: data
          persistentVolumeClaim:
            claimName: elasticsearch-data
---
apiVersion: v1
kind: Service
metadata:
  name: elasticsearch
spec:
  selector: { app: elasticsearch }
  ports:
    - port: 9200
      targetPort: 9200
```

Segurança desligada porque é nó único de laboratório, sem exposição externa. Registrar como dívida: ligar `xpack.security` e credencial via Secret antes de qualquer dado real.

- [ ] **Step 11: Escalar o cluster se faltar memória**

```powershell
kubectl top nodes
az aks scale -n fcg-aks -g fcg-rg --node-count 4
```

Esperado sem escala: todos os pods `Running`. Se algum ficar `Pending` com evento `Insufficient memory`, este é o remédio.

- [ ] **Step 12: Verificar fuzzy e relevância**

```powershell
$ip = kubectl get svc kong -o jsonpath='{.status.loadBalancer.ingress[0].ip}'
curl.exe -s "http://$ip`:8000/api/jogos/search?q=jgoo" -H "Authorization: Bearer $token"
```

Esperado: o jogo "Jogo ..." na primeira posição, apesar do erro de digitação.

- [ ] **Step 13: Commit**

```powershell
git add -A
git commit -m "feat(catalog): busca com Elasticsearch, fuzzy e indexação na escrita"
```

---

### Task 6: Fechar as falhas de autenticação do catálogo e do login

**Files:**
- Modify: `fcg-catalog-api\FCG.CatalogAPI.API\Controllers\GamesController.cs:L87-L105`
- Modify: `fcg-catalog-api\FCG.CatalogAPI.API\Controllers\BibliotecaController.cs:L19-L32`
- Modify: `fcg-users-api\FCG.UsersAPI.Application\Services\AuthService.cs:L27-L33`
- Create: `fcg-catalog-api\FCG.CatalogAPI.Tests\AutorizacaoTests.cs`
- Create: `fcg-users-api\FCG.UsersAPI.Tests\AuthServiceTests.ErroTest.cs`

**Interfaces:**
- Consumes: claim `"Id"` do token, o mesmo lido em `AvaliacoesController.cs:L51-L58`.
- Produces: compra e leitura de biblioteca sempre no usuário do token; login com e-mail inexistente devolve 401 em vez de 500.

Fora do escopo: idempotência dos consumidores de pagamento. Está na lista de dívidas do relatório de auditoria.

- [ ] **Step 1: Teste de que o corpo não define o usuário**

```csharp
using System.Security.Claims;
using FCG.CatalogAPI.API.Controllers;
using FCG.CatalogAPI.Application.DTOs;
using FCG.CatalogAPI.Application.Services;
using FCG.CatalogAPI.Domain.Entities;
using FCG.CatalogAPI.Domain.Interfaces;
using FCG.Shared.Events;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

public class AutorizacaoTests
{
    [Fact]
    public async Task Comprar_DeveIgnorarUserIdDoCorpo()
    {
        var userDoToken = Guid.NewGuid();
        OrderPlacedEvent? publicado = null;

        var games = new Mock<IGameRepository>();
        games.Setup(g => g.ObterPorId(It.IsAny<Guid>())).ReturnsAsync(new Game("Jogo", "Desc", 10m));
        var userGames = new Mock<IUserGameRepository>();
        userGames.Setup(u => u.ObterPorIds(It.IsAny<Guid>(), It.IsAny<Guid>())).ReturnsAsync((UserGame?)null);
        var publish = new Mock<IPublishEndpoint>();
        publish.Setup(p => p.Publish(It.IsAny<OrderPlacedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OrderPlacedEvent, CancellationToken>((e, _) => publicado = e)
            .Returns(Task.CompletedTask);

        var service = new GameService(
            games.Object, userGames.Object, publish.Object,
            Mock.Of<IGameSearchIndex>(), NullLogger<GameService>.Instance);

        var controller = new GamesController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("Id", userDoToken.ToString()) }, "test"))
                }
            }
        };

        await controller.Comprar(Guid.NewGuid(), new IniciarCompraDTO { UserId = Guid.NewGuid() });

        Assert.Equal(userDoToken, publicado!.UserId);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar** — esperado: `Assert.Equal` falha, o evento sai com o id do corpo.

- [ ] **Step 3: Usar o token**

```csharp
[HttpPost("{id:guid}/comprar")]
[Authorize]
public async Task<IActionResult> Comprar(Guid id, [FromBody] IniciarCompraDTO dto)
{
    dto.GameId = id;
    dto.UserId = ObterUsuarioId();
    var mensagem = await _gameService.IniciarCompra(dto);
    return Accepted(new { mensagem });
}
```

Copiar `ObterUsuarioId()` de `AvaliacoesController.cs:L51-L58` para este controller.

- [ ] **Step 4: Checar dono da biblioteca**

Copiar `VerificarAcesso` de `FCG.API\Controllers\BibliotecaController.cs:L64-L72` (Admin passa, dono passa, resto 403), chamar no início de `ObterBiblioteca` e remover o `try/catch` genérico — o `ErrorHandlingMiddleware` já mapeia as exceções.

- [ ] **Step 5: Teste do login com e-mail inexistente**

```csharp
[Fact]
public async Task GerarToken_UsuarioInexistente_DeveLancarUnauthorized()
{
    var repo = new Mock<IUserRepository>();
    repo.Setup(r => r.ObterPorEmail(It.IsAny<string>())).ReturnsAsync((User?)null);
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Jwt:SecretKey"] = "chave-de-teste-com-32-caracteres!!",
        ["Jwt:Issuer"] = "FCG.UsersAPI",
        ["Jwt:Audience"] = "FCG.Client",
        ["Jwt:ExpiracaoHoras"] = "8"
    }).Build();

    await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
        new AuthService(repo.Object, config).GerarToken(new LoginDTO { Email = "nao@existe.com", Senha = "Qualquer@123" }));
}
```

- [ ] **Step 6: Rodar e ver falhar** — hoje lança `InvalidOperationException` (`AuthService.cs:L28-L29`), que o middleware converte em **500**.

- [ ] **Step 7: Unificar o erro de credencial**

```csharp
var senhaValida = user is not null && BCrypt.Net.BCrypt.Verify(dto.Senha, user.SenhaHash);
if (!senhaValida)
    throw new UnauthorizedAccessException("E-mail ou senha inválidos.");
```

Substitui os dois `throw` das linhas `L28-L33`. Mesma mensagem para os dois casos, e o texto deixa de revelar se a conta existe — o monolito já faz assim (`FCG.Application\Services\AuthService.cs:L28-L34`) e o `users-api` divergiu na extração.

Limite aceito: o caminho "usuário não existe" não executa o hash, então o tempo de resposta ainda difere. Corrigir exige hash de descarte; fora do escopo desta fase.

- [ ] **Step 8: Rodar tudo e commitar**

```powershell
dotnet test
git commit -am "fix: compra e biblioteca pelo token; login sempre 401"
```

Esperado: falhas 0 nos dois repos.

---

### Task 7: Zero credenciais no código

**Files:**
- Modify: `fcg-users-api\FCG.UsersAPI.API\Program.cs:L86-L90`
- Modify: `fcg-catalog-api\FCG.CatalogAPI.API\Program.cs:L92-L96`
- Modify: `fcg-payments-api\FCG.PaymentsAPI.API\Program.cs:L30-L34`
- Modify: `fcg-notifications-api\FCG.NotificationsAPI.API\Program.cs:L23-L27`
- Modify: `fcg-orchestration\k8s\rabbitmq-deployment.yaml:L46-L50`
- Modify: `fcg-orchestration\k8s\{users,catalog,payments}-api-deployment.yaml`
- Modify: `fcg-orchestration\docker-compose.yml:L4-L16`
- Create: `fcg-orchestration\.env.example` (incluir `RABBITMQ_USER` e `RABBITMQ_PASS`)

**Interfaces:**
- Produces: os quatro serviços falham no boot se `RABBITMQ_USER`/`RABBITMQ_PASS` não existirem; manifestos leem de Secret.

- [ ] **Step 1: Trocar o literal por configuração**

```csharp
var rabbitUser = Environment.GetEnvironmentVariable("RABBITMQ_USER")
    ?? throw new InvalidOperationException("A variável RABBITMQ_USER não foi configurada.");
var rabbitPass = Environment.GetEnvironmentVariable("RABBITMQ_PASS")
    ?? throw new InvalidOperationException("A variável RABBITMQ_PASS não foi configurada.");
```

Dentro do `cfg.Host(...)`: `h.Username(rabbitUser); h.Password(rabbitPass);`. Falhar no boot é melhor que subir com credencial default e falhar no primeiro evento.

- [ ] **Step 2: Secret do broker e injeção nos manifestos**

```powershell
kubectl create secret generic rabbitmq-secret `
  --from-literal=user=fcg --from-literal=pass=<senha-forte> `
  --dry-run=client -o yaml | kubectl apply -f -
```

No `rabbitmq-deployment.yaml`, trocar os literais por `secretKeyRef` (`rabbitmq-secret`/`user` e `pass`); nos deployments dos serviços, acrescentar as duas variáveis com `secretKeyRef`. Não commitar arquivo com o valor da senha.

- [ ] **Step 3: Atualizar o compose e o `.env.example`**

`docker-compose.yml` passa a ler `RABBITMQ_USER`/`RABBITMQ_PASS` do `.env`, junto de `SA_PASSWORD` e `JWT_SECRET` que já estão lá.

- [ ] **Step 4: Verificar que nada sobrou**

```powershell
Get-ChildItem -Path .. -Recurse -File -Include *.cs,*.json,*.yaml,*.yml |
  Where-Object { $_.FullName -notmatch '\\(bin|obj|Migrations)\\' } |
  Select-String -Pattern 'Password\("|Username\("|guest:guest|SA_PASSWORD='
```

Executar de `fcg-orchestration`. Esperado: nenhuma ocorrência em código ou manifesto (`.env.example` com placeholder é aceitável).

- [ ] **Step 5: Rodar tudo de novo e commitar**

```powershell
docker compose up -d --build
git commit -am "feat(security): credenciais do broker por variável e Secret"
```

---

## Ordem de execução

1 → 2 → 3 → 4 (espinha da entrega: cluster, deploy manual, testes, automação).
5 e 6 podem rodar em paralelo com 1–4, porque não dependem da nuvem; 6 é o mais curto e o de maior risco.
7 depende do cluster estar de pé, porque o Step 2 precisa de `kubectl` configurado.

## Riscos assumidos

- SQL Server, Mongo, Redis, RabbitMQ e Elasticsearch rodam como containers com PVC, não como serviços gerenciados. Custa menos e mantém a arquitetura; backup e HA ficam por conta do cluster.
- AKS com 3 nós `Standard_B2ms` fica no limite de memória com Elasticsearch ligado. Plano B: escalar para 4 nós (Task 5, Step 11) ou desligar o Loki.
- `xpack.security.enabled=false` no Elasticsearch é aceitável só neste laboratório.
- O caminho "usuário inexistente" do login continua sem custo de hash, então o tempo de resposta ainda distingue conta existente de inexistente.
