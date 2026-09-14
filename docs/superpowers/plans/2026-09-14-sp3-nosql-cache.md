# SP3 — Persistência poliglota (MongoDB) + Cache distribuído (Redis): plano de execução

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Entregar os dois requisitos restantes de dados da Fase 3 — persistência poliglota (avaliações de jogos em MongoDB) e cache distribuído (leitura do catálogo em Redis, com degradação graciosa) — no `catalog-api`, com infraestrutura declarativa no `fcg-orchestration`.

**Architecture:** As avaliações são um agregado próprio dentro do `catalog-api` (o requisito é de persistência poliglota, não de um quarto serviço): entidade `Review` no domínio, `IReviewRepository` no domínio, `MongoReviewRepository` na infraestrutura, `ReviewService` na aplicação e `AvaliacoesController` na API, sob a rota `api/jogos/{gameId}/avaliacoes` — que o Kong já cobre sem alteração. O cache entra por **decorator**: `CachedGameRepository : IGameRepository` envolve o `GameRepository` e é registrado no DI, então o `GameService` não muda uma linha e a degradação (Redis fora → vai ao SQL) fica isolada numa única classe.

**Tech Stack:** .NET 8 (`net8.0`), `MongoDB.Driver` 3.11.2, `Microsoft.Extensions.Caching.StackExchangeRedis` 8.0.31 (`IDistributedCache`), `prometheus-net.AspNetCore` 8.2.1 (já presente), MongoDB 8.0.30 e Redis 7.4.11 no Kubernetes do Docker Desktop, Kong DB-less (inalterado).

**Spec:** `docs/superpowers/specs/2026-09-11-fase3-gateway-serverless-observabilidade-design.md` (seções §113-120 e decisões D4, D5, D9; restrições §6 e §9)

## Global Constraints

- Namespace **`default`** em tudo. Nada de `namespace:` novo nos manifestos.
- Serviços **HTTP apenas**: não definir porta HTTPS em `Service` (as APIs seguem em `8080`, o Service publica `80`).
- **Segredo nunca no git.** `mongo-secret` só existe por comando documentado (`kubectl create secret ... --dry-run=client -o yaml | kubectl apply -f -`). Antes de cada commit: `git grep -i -E 'password|senha|secret'` no repo tocado e conferir que nenhum valor real entrou (os `*-secret.yaml` pré-existentes são dívida registrada da fase, não deste SP).
- **Tag de imagem versionada e nunca reusada:** `fcg-catalog-api:sp3-<sha7>`, onde `<sha7>` é o commit daquele build. `:latest` e `:sp2` estão **proibidos**. `imagePullPolicy: IfNotPresent` (com `:latest` o kubelet reusa a imagem em cache e o rollout sobe o binário velho — foi o defeito que travou o SP2).
- O **build da imagem é o gate de compilação**: `docker build -t fcg-catalog-api:sp3-<sha7> ../fcg-catalog-api` a partir de `fcg-orchestration` (o Dockerfile da raiz faz `dotnet publish` da solução inteira). Não é preciso `dotnet` local.
- **Nenhuma suíte de teste nova** (D8 da spec). A verificação é por execução real no cluster.
- Scripts `.ps1` de verificação: **ASCII puro** (sem em dash, sem aspas curvas) — arquivo UTF-8 sem BOM quebra o parser do PowerShell 5.1.
- Corpo JSON em `curl.exe` no PowerShell vai **por arquivo** (`-d '@corpo.json'`): inline, as aspas são comidas e a API responde `400` de payload inválido.
- Subagentes **editam e commitam**; o controlador roda `docker`, `kubectl` e `curl` (subagentes não têm permissão e não devem tentar).
- Sem `--amend`, sem force-push, sem push direto em `master`: branch + PR.
- Kong **não muda**: a rota `catalog-jogos` cobre `/api/jogos` (prefixo, qualquer método) com o plugin JWT.
- A `users-api` **não é tocada**.
- Comentários, mensagens de erro e documentação em pt-BR, como o resto do projeto.

## Mapa de arquivos

**`fcg-catalog-api`** (branch `fase3/sp3-nosql-cache`)

| Arquivo | Responsabilidade |
|---|---|
| `FCG.CatalogAPI.Domain/Entities/Review.cs` (criar) | Agregado da avaliação — C# puro, sem atributo de persistência |
| `FCG.CatalogAPI.Domain/Entities/ReviewResumo.cs` (criar) | `record ReviewResumo(int Total, double? NotaMedia)` |
| `FCG.CatalogAPI.Domain/Interfaces/IReviewRepository.cs` (criar) | Contrato do repositório (upsert, listar, resumo, inicializar índices) |
| `FCG.CatalogAPI.Domain/Entities/Game.cs` (modificar) | Construtor de reidratação usado pelo cache (Task 4) |
| `FCG.CatalogAPI.Infrastructure/Data/ReviewDocument.cs` (criar) | Documento BSON (atributos do driver ficam na infraestrutura) |
| `FCG.CatalogAPI.Infrastructure/Repositories/MongoReviewRepository.cs` (criar) | Upsert atômico, listagem, aggregation do resumo, índices |
| `FCG.CatalogAPI.Infrastructure/Cache/GameCacheItem.cs` (criar) | Payload serializável do cache (Task 4) |
| `FCG.CatalogAPI.Infrastructure/Repositories/CachedGameRepository.cs` (criar) | Decorator de cache com hit/miss e degradação (Task 4) |
| `FCG.CatalogAPI.Application/DTOs/CriarAvaliacaoDTO.cs` (criar) | Entrada do PUT (nota, comentário, tags) |
| `FCG.CatalogAPI.Application/DTOs/ReviewResponseDTO.cs` (criar) | Saída da avaliação (inclui `usuarioId`) |
| `FCG.CatalogAPI.Application/DTOs/ReviewResumoDTO.cs` (criar) | Saída do resumo (total e média) |
| `FCG.CatalogAPI.Application/Services/ReviewService.cs` (criar) | Regras: jogo existe, nota 1-5, upsert por usuário/jogo |
| `FCG.CatalogAPI.API/Controllers/AvaliacoesController.cs` (criar) | Endpoints REST sob `api/jogos/{gameId:guid}/avaliacoes` |
| `FCG.CatalogAPI.API/Program.cs` (modificar) | DI do Mongo/Redis, decorator do repositório, bootstrap dos índices |
| `FCG.CatalogAPI.API/appsettings.json` (modificar) | Defaults de desenvolvimento (`localhost`) para Mongo e Redis |
| `FCG.CatalogAPI.Infrastructure/FCG.CatalogAPI.Infrastructure.csproj` (modificar) | Pacotes `MongoDB.Driver` e `Microsoft.Extensions.Caching.StackExchangeRedis` |

**`fcg-orchestration`** (branch `fase3/sp3-nosql-cache`)

| Arquivo | Responsabilidade |
|---|---|
| `k8s/mongo-deployment.yaml` (criar) | Deployment + PVC `mongo-data` (1Gi) + Service `mongo:27017` |
| `k8s/redis-deployment.yaml` (criar) | Deployment + Service `redis:6379` (sem PVC: é cache) |
| `k8s/catalog-api-configmap.yaml` (modificar) | `Mongo__DatabaseName`, `Redis__ConnectionString` |
| `k8s/catalog-api-deployment.yaml` (modificar) | Imagem `:sp3-<sha7>` e `Mongo__ConnectionString` via Secret |
| `README.md` (modificar) | Seção de persistência poliglota e cache, portas, estrutura, segredos |

**Ledger SDD (git-ignored, no repo `fcg`):** `.superpowers/sdd/2026-09-14-sp3-nosql-cache/` — `progress.md`, briefs, relatórios, revisões, diffs, logs de runtime e os scripts `verify-sp3-*.ps1`.

---

### Task 1: MongoDB e Redis no cluster

**Files:**
- Create: `k8s/mongo-deployment.yaml`, `k8s/redis-deployment.yaml`

**Interfaces:**
- Consumes: nada (infraestrutura pura).
- Produces: Service `mongo` (`ClusterIP:27017`), Service `redis` (`ClusterIP:6379`), PVC `mongo-data`, e o `Secret mongo-secret` com as chaves `root-username`, `root-password` e `connection-string` — consumidos pelas Tasks 2, 3, 4 e 5.

- [ ] **Step 1: Criar o Secret do Mongo (controlador; comando documentado, nunca no git)**

```powershell
kubectl create secret generic mongo-secret `
  --from-literal=root-username=fcg `
  --from-literal=root-password='FcgMongo2026' `
  --from-literal=connection-string='mongodb://fcg:FcgMongo2026@mongo:27017/?authSource=admin' `
  --dry-run=client -o yaml | kubectl apply -f -
kubectl get secret mongo-secret -o jsonpath='{.data.connection-string}' | ForEach-Object { [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($_)) }
```

A senha é alfanumérica de propósito: caractere especial (ex.: `@`) teria que vir percent-encoded na URI (`%40`) e é uma fonte clássica de erro silencioso de conexão.

- [ ] **Step 2: Escrever `k8s/mongo-deployment.yaml`**

```yaml
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: mongo-data
  labels:
    app: mongo
spec:
  accessModes:
    - ReadWriteOnce
  storageClassName: standard
  resources:
    requests:
      storage: 1Gi
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: mongo
  labels:
    app: mongo
spec:
  replicas: 1
  # O PVC é ReadWriteOnce: num rolling update o pod novo ficaria preso esperando o volume.
  strategy:
    type: Recreate
  selector:
    matchLabels:
      app: mongo
  template:
    metadata:
      labels:
        app: mongo
    spec:
      containers:
        - name: mongo
          image: mongo:8.0.30
          imagePullPolicy: IfNotPresent
          ports:
            - name: mongo
              containerPort: 27017
          env:
            - name: MONGO_INITDB_ROOT_USERNAME
              valueFrom:
                secretKeyRef:
                  name: mongo-secret
                  key: root-username
            - name: MONGO_INITDB_ROOT_PASSWORD
              valueFrom:
                secretKeyRef:
                  name: mongo-secret
                  key: root-password
          readinessProbe:
            tcpSocket:
              port: mongo
            initialDelaySeconds: 10
            periodSeconds: 5
          livenessProbe:
            tcpSocket:
              port: mongo
            initialDelaySeconds: 30
            periodSeconds: 10
          resources:
            limits:
              memory: 512Mi
              cpu: 500m
          volumeMounts:
            - name: data
              mountPath: /data/db
      volumes:
        - name: data
          persistentVolumeClaim:
            claimName: mongo-data
---
apiVersion: v1
kind: Service
metadata:
  name: mongo
  labels:
    app: mongo
spec:
  type: ClusterIP
  selector:
    app: mongo
  ports:
    - name: mongo
      port: 27017
      targetPort: mongo
```

- [ ] **Step 3: Escrever `k8s/redis-deployment.yaml`**

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: redis
  labels:
    app: redis
spec:
  replicas: 1
  selector:
    matchLabels:
      app: redis
  template:
    metadata:
      labels:
        app: redis
    spec:
      containers:
        - name: redis
          image: redis:7.4.11-alpine3.21
          imagePullPolicy: IfNotPresent
          # Cache: sem PVC de propósito (perder o conteúdo é aceitável) e com teto de memória
          # e política de descarte explícitos, para não virar um vazamento silencioso no nó.
          args:
            - redis-server
            - --maxmemory
            - 128mb
            - --maxmemory-policy
            - allkeys-lru
          ports:
            - name: redis
              containerPort: 6379
          readinessProbe:
            tcpSocket:
              port: redis
            initialDelaySeconds: 5
            periodSeconds: 5
          livenessProbe:
            tcpSocket:
              port: redis
            initialDelaySeconds: 20
            periodSeconds: 10
          resources:
            limits:
              memory: 192Mi
              cpu: 250m
---
apiVersion: v1
kind: Service
metadata:
  name: redis
  labels:
    app: redis
spec:
  type: ClusterIP
  selector:
    app: redis
  ports:
    - name: redis
      port: 6379
      targetPort: redis
```

- [ ] **Step 4: Aplicar e esperar ficarem prontos (controlador)**

```powershell
kubectl apply -f k8s/mongo-deployment.yaml
kubectl apply -f k8s/redis-deployment.yaml
kubectl rollout status deployment/mongo --timeout=180s
kubectl rollout status deployment/redis --timeout=120s
kubectl get pvc mongo-data
kubectl get pods -l 'app in (mongo,redis)'
```

Esperado: `deployment "mongo" successfully rolled out`, PVC `Bound`, os dois pods `1/1`.

- [ ] **Step 5: Verificar serviço de verdade (não só o pod)**

```powershell
kubectl exec deploy/mongo -- mongosh -u fcg -p FcgMongo2026 --authenticationDatabase admin --quiet --eval "db.runCommand({ ping: 1 })"
kubectl exec deploy/redis -- redis-cli ping
kubectl exec deploy/redis -- redis-cli config get maxmemory-policy
```

Esperado: `{ ok: 1 }`, `PONG` e `allkeys-lru`. Sem o `-u/-p` o `mongosh` conecta mas as operações falham — é assim que se descobre que o `MONGO_INITDB_*` não rodou (ele só roda com o data dir vazio).

- [ ] **Step 6: Commit**

```bash
git add k8s/mongo-deployment.yaml k8s/redis-deployment.yaml
git commit -m "feat: adiciona mongo com pvc e redis sem persistencia ao cluster"
```

---

### Task 2: Domínio de avaliações, repositório Mongo e wiring

**Files:**
- Create: `FCG.CatalogAPI.Domain/Entities/Review.cs`, `FCG.CatalogAPI.Domain/Entities/ReviewResumo.cs`, `FCG.CatalogAPI.Domain/Interfaces/IReviewRepository.cs`, `FCG.CatalogAPI.Infrastructure/Data/ReviewDocument.cs`, `FCG.CatalogAPI.Infrastructure/Repositories/MongoReviewRepository.cs`
- Modify: `FCG.CatalogAPI.Infrastructure/FCG.CatalogAPI.Infrastructure.csproj`, `FCG.CatalogAPI.API/Program.cs`, `FCG.CatalogAPI.API/appsettings.json`, `k8s/catalog-api-configmap.yaml`, `k8s/catalog-api-deployment.yaml` (repo `fcg-orchestration`)

**Interfaces:**
- Consumes: Service `mongo` e `Secret mongo-secret` (Task 1).
- Produces (assinaturas usadas pelas Tasks 3 e 4):
  - `Review`: `Guid Id`, `Guid GameId`, `Guid UserId`, `int Nota`, `string? Comentario`, `List<string> Tags`, `DateTime DataCriacao`, `DateTime DataAtualizacao` (todos com get/set públicos — o driver precisa materializar).
  - `ReviewResumo`: `record ReviewResumo(int Total, double? NotaMedia)`.
  - `IReviewRepository`: `Task<bool> UpsertAsync(Review review)` (true = criou), `Task<IEnumerable<Review>> ObterPorJogoAsync(Guid gameId)`, `Task<ReviewResumo> ObterResumoAsync(Guid gameId)`, `Task InicializarAsync()`.
  - DI: `IMongoClient` (singleton), `IReviewRepository` (scoped); configuração `Mongo:ConnectionString` (obrigatória) e `Mongo:DatabaseName` (default `fcg_catalog`).

- [ ] **Step 1: Adicionar o pacote do driver**

Em `FCG.CatalogAPI.Infrastructure/FCG.CatalogAPI.Infrastructure.csproj`, dentro do `<ItemGroup>` de pacotes:

```xml
    <PackageReference Include="MongoDB.Driver" Version="3.11.2" />
```

- [ ] **Step 2: Escrever a entidade de domínio `Review.cs`**

```csharp
namespace FCG.CatalogAPI.Domain.Entities;

/// <summary>
/// Avaliação de um jogo por um usuário. Vive no MongoDB (persistência poliglota):
/// o schema é flexível de propósito (comentário opcional e lista de tags livre),
/// o que não caberia confortavelmente no modelo relacional do catálogo.
/// Sem atributos de persistência aqui: o mapeamento BSON fica na infraestrutura.
/// </summary>
public class Review
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GameId { get; set; }
    public Guid UserId { get; set; }
    public int Nota { get; set; }
    public string? Comentario { get; set; }
    public List<string> Tags { get; set; } = new();
    public DateTime DataCriacao { get; set; }
    public DateTime DataAtualizacao { get; set; }
}
```

- [ ] **Step 3: Escrever `ReviewResumo.cs`**

```csharp
namespace FCG.CatalogAPI.Domain.Entities;

/// <summary>
/// Resultado agregado das avaliações de um jogo, calculado no próprio MongoDB
/// (pipeline de agregação) em vez de trazer os documentos para somar na aplicação.
/// </summary>
public record ReviewResumo(int Total, double? NotaMedia);
```

- [ ] **Step 4: Escrever `IReviewRepository.cs`**

```csharp
using FCG.CatalogAPI.Domain.Entities;

namespace FCG.CatalogAPI.Domain.Interfaces;

public interface IReviewRepository
{
    /// <summary>Insere ou substitui a avaliação do par (jogo, usuário). Retorna true quando criou.</summary>
    Task<bool> UpsertAsync(Review review);

    Task<IEnumerable<Review>> ObterPorJogoAsync(Guid gameId);

    Task<ReviewResumo> ObterResumoAsync(Guid gameId);

    /// <summary>Cria o índice único de (jogo, usuário). Chamado uma vez no boot da API.</summary>
    Task InicializarAsync();
}
```

- [ ] **Step 5: Escrever `ReviewDocument.cs` (mapeamento BSON)**

```csharp
using FCG.CatalogAPI.Domain.Entities;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace FCG.CatalogAPI.Infrastructure.Data;

/// <summary>
/// Representação do documento na coleção "avaliacoes". Os atributos do driver ficam
/// aqui, na infraestrutura, para o domínio continuar sem dependência de MongoDB.
/// Guid como string (BsonType.String) para o documento ficar legível no mongosh.
/// </summary>
public class ReviewDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    [BsonElement("gameId")]
    [BsonRepresentation(BsonType.String)]
    public Guid GameId { get; set; }

    [BsonElement("userId")]
    [BsonRepresentation(BsonType.String)]
    public Guid UserId { get; set; }

    [BsonElement("nota")]
    public int Nota { get; set; }

    [BsonElement("comentario")]
    [BsonIgnoreIfNull]
    public string? Comentario { get; set; }

    [BsonElement("tags")]
    public List<string> Tags { get; set; } = new();

    [BsonElement("dataCriacao")]
    public DateTime DataCriacao { get; set; }

    [BsonElement("dataAtualizacao")]
    public DateTime DataAtualizacao { get; set; }

    public static ReviewDocument De(Review review) => new()
    {
        Id = review.Id,
        GameId = review.GameId,
        UserId = review.UserId,
        Nota = review.Nota,
        Comentario = review.Comentario,
        Tags = review.Tags,
        DataCriacao = review.DataCriacao,
        DataAtualizacao = review.DataAtualizacao
    };

    public Review ParaEntidade() => new()
    {
        Id = Id,
        GameId = GameId,
        UserId = UserId,
        Nota = Nota,
        Comentario = Comentario,
        Tags = Tags,
        DataCriacao = DataCriacao,
        DataAtualizacao = DataAtualizacao
    };
}
```

- [ ] **Step 6: Escrever `MongoReviewRepository.cs`**

```csharp
using FCG.CatalogAPI.Domain.Entities;
using FCG.CatalogAPI.Domain.Interfaces;
using FCG.CatalogAPI.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FCG.CatalogAPI.Infrastructure.Repositories;

public class MongoReviewRepository : IReviewRepository
{
    private const string NomeColecao = "avaliacoes";

    private readonly IMongoCollection<ReviewDocument> _colecao;
    private readonly ILogger<MongoReviewRepository> _logger;

    public MongoReviewRepository(IMongoDatabase database, ILogger<MongoReviewRepository> logger)
    {
        _colecao = database.GetCollection<ReviewDocument>(NomeColecao);
        _logger = logger;
    }

    public async Task InicializarAsync()
    {
        var chaves = Builders<ReviewDocument>.IndexKeys
            .Ascending(r => r.GameId)
            .Ascending(r => r.UserId);

        // Único: garante "uma avaliação por usuário e jogo" mesmo se duas requisições
        // do mesmo usuário chegarem juntas (o upsert sozinho não bastaria).
        await _colecao.Indexes.CreateOneAsync(
            new CreateIndexModel<ReviewDocument>(chaves, new CreateIndexOptions { Unique = true }));

        _logger.LogInformation("Indice unico (gameId, userId) garantido na colecao {Colecao}.", NomeColecao);
    }

    public async Task<bool> UpsertAsync(Review review)
    {
        var filtro = Builders<ReviewDocument>.Filter.Eq(r => r.GameId, review.GameId)
                     & Builders<ReviewDocument>.Filter.Eq(r => r.UserId, review.UserId);

        // Uma única operação atômica: cria na primeira vez (SetOnInsert preserva a data
        // original) e atualiza nas seguintes.
        var atualizacao = Builders<ReviewDocument>.Update
            .Set(r => r.Nota, review.Nota)
            .Set(r => r.Comentario, review.Comentario)
            .Set(r => r.Tags, review.Tags)
            .Set(r => r.DataAtualizacao, review.DataAtualizacao)
            .SetOnInsert(r => r.Id, review.Id)
            .SetOnInsert(r => r.DataCriacao, review.DataCriacao);

        var resultado = await _colecao.UpdateOneAsync(filtro, atualizacao, new UpdateOptions { IsUpsert = true });

        var criou = resultado.UpsertedId is not null;
        _logger.LogInformation("Avaliacao {Acao} para o jogo {GameId} pelo usuario {UserId}.",
            criou ? "criada" : "atualizada", review.GameId, review.UserId);

        return criou;
    }

    public async Task<IEnumerable<Review>> ObterPorJogoAsync(Guid gameId)
    {
        var documentos = await _colecao
            .Find(Builders<ReviewDocument>.Filter.Eq(r => r.GameId, gameId))
            .SortByDescending(r => r.DataAtualizacao)
            .ToListAsync();

        return documentos.Select(d => d.ParaEntidade()).ToList();
    }

    public async Task<ReviewResumo> ObterResumoAsync(Guid gameId)
    {
        // O $group é montado como BsonDocument e a pipeline declara o serializador de saída
        // explicitamente: PipelineDefinition<TInput, TOutput>.Create(IEnumerable<BsonDocument>,
        // IBsonSerializer<TOutput>) é a sobrecarga documentada da API do driver.
        var estagios = new BsonDocument[]
        {
            new("$match", new BsonDocument("gameId", gameId.ToString())),
            new("$group", new BsonDocument
            {
                { "_id", BsonNull.Value },
                { "total", new BsonDocument("$sum", 1) },
                { "notaMedia", new BsonDocument("$avg", "$nota") }
            })
        };

        var pipeline = PipelineDefinition<ReviewDocument, BsonDocument>.Create(
            estagios,
            BsonDocumentSerializer.Instance);

        var resultado = await _colecao.Aggregate(pipeline).FirstOrDefaultAsync();
        if (resultado is null)
            return new ReviewResumo(0, null);

        return new ReviewResumo(
            resultado["total"].ToInt32(),
            Math.Round(resultado["notaMedia"].ToDouble(), 2));
    }
}
```

Com estes `using` adicionais no topo do arquivo:

```csharp
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
```

> O `$avg` sobre um campo `int` devolve `double` no BSON, então o acesso correto é `ToDouble()` — um `ToInt32()` ali truncaria a média (3,5 viraria 3) e quebraria o resumo.

- [ ] **Step 7: Registrar no DI e inicializar os índices em `Program.cs`**

Depois do bloco de `AddScoped` dos repositórios existentes (linha ~104) e antes de `builder.Services.AddHealthChecks();`:

```csharp
var mongoConnectionString = builder.Configuration["Mongo:ConnectionString"]
    ?? throw new InvalidOperationException("A configuração Mongo:ConnectionString não foi encontrada.");
var mongoDatabaseName = builder.Configuration["Mongo:DatabaseName"] ?? "fcg_catalog";

builder.Services.AddSingleton<IMongoClient>(_ =>
    new MongoClient(mongoConnectionString));
builder.Services.AddScoped<IMongoDatabase>(sp =>
    sp.GetRequiredService<IMongoClient>().GetDatabase(mongoDatabaseName));
builder.Services.AddScoped<IReviewRepository, MongoReviewRepository>();
```

Com os `using` no topo do arquivo:

```csharp
using MongoDB.Driver;
```

E no bloco de inicialização que já existe (o que roda `db.Database.Migrate()`), acrescentar:

```csharp
try
{
    var repositorioAvaliacoes = scope.ServiceProvider.GetRequiredService<IReviewRepository>();
    await repositorioAvaliacoes.InicializarAsync();
}
catch (Exception ex)
{
    // Mongo fora do ar não pode impedir o catálogo (SQL) de subir: degrada como o cache.
    app.Logger.LogError(ex, "Nao foi possivel inicializar a colecao de avaliacoes no MongoDB.");
}
```

> A falha de **configuração** (sem connection string) derruba o boot de propósito, como já acontece com `Jwt:SecretKey`; a falha de **infraestrutura** (Mongo inalcançável) só loga.

- [ ] **Step 8: Adicionar defaults de desenvolvimento em `appsettings.json`**

```json
  "Mongo": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "fcg_catalog"
  },
```

- [ ] **Step 9: Configuração do cluster**

Em `fcg-orchestration/k8s/catalog-api-configmap.yaml`, adicionar ao `data`:

```yaml
  Mongo__DatabaseName: "fcg_catalog"
```

Em `fcg-orchestration/k8s/catalog-api-deployment.yaml`, no bloco `env` (junto do `ConnectionStrings__DefaultConnection`), e trocar a imagem:

```yaml
          image: fcg-catalog-api:sp3-<sha7>
```

```yaml
            - name: Mongo__ConnectionString
              valueFrom:
                secretKeyRef:
                  name: mongo-secret
                  key: connection-string
```

- [ ] **Step 10: Buildar, aplicar e verificar índice e saúde (controlador)**

O `<sha7>` é o commit deste task. Buildar, atualizar a tag no manifesto, commitar e aplicar:```powershell
$sha = (git -C .fase2-repos\fcg-catalog-api rev-parse --short HEAD).Substring(0,7)
Set-Location .fase2-repos\fcg-orchestration
docker build -t "fcg-catalog-api:sp3-$sha" ../fcg-catalog-api
kubectl apply -f k8s/catalog-api-configmap.yaml -f k8s/catalog-api-deployment.yaml
kubectl rollout status deployment/catalog-api --timeout=300s
kubectl exec deploy/mongo -- mongosh -u fcg -p FcgMongo2026 --authenticationDatabase admin --quiet --eval "db.getSiblingDB('fcg_catalog').avaliacoes.getIndexes()"
```

Esperado: imagem buildada sem erro de compilação, `rollout status` OK, pod `1/1`, e o índice `gameId_1_userId_1` com `"unique": true`. Se o pod ficar `0/1`, ver `kubectl logs deploy/catalog-api` — `Mongo:ConnectionString não foi encontrada` significa que a env não chegou (Secret ausente).

- [ ] **Step 11: Verificar que nada quebrou (o cache e as avaliações ainda não existem, mas o catálogo tem que continuar igual)**

```powershell
kubectl port-forward svc/catalog-api 18081:80
curl.exe -s -o NUL -w '%{http_code}' http://localhost:18081/health
curl.exe -s -o NUL -w '%{http_code}' http://localhost:18081/metrics
```

Esperado: `200` e `200`.

- [ ] **Step 12: Commit**

```bash
git add FCG.CatalogAPI.Domain/Entities/Review.cs FCG.CatalogAPI.Domain/Entities/ReviewResumo.cs FCG.CatalogAPI.Domain/Interfaces/IReviewRepository.cs FCG.CatalogAPI.Infrastructure/Data/ReviewDocument.cs FCG.CatalogAPI.Infrastructure/Repositories/MongoReviewRepository.cs FCG.CatalogAPI.Infrastructure/FCG.CatalogAPI.Infrastructure.csproj FCG.CatalogAPI.API/Program.cs FCG.CatalogAPI.API/appsettings.json
git commit -m "feat: dominio de avaliacoes com repositorio mongodb e indice unico"
```

---

### Task 3: `ReviewService` e `AvaliacoesController`

**Files:**
- Create: `FCG.CatalogAPI.Application/DTOs/CriarAvaliacaoDTO.cs`, `FCG.CatalogAPI.Application/DTOs/ReviewResponseDTO.cs`, `FCG.CatalogAPI.Application/DTOs/ReviewResumoDTO.cs`, `FCG.CatalogAPI.Application/Services/ReviewService.cs`, `FCG.CatalogAPI.API/Controllers/AvaliacoesController.cs`
- Modify: `FCG.CatalogAPI.API/Program.cs` (registrar o `ReviewService`), `k8s/catalog-api-deployment.yaml` (nova tag)

**Interfaces:**
- Consumes: `IReviewRepository`, `Review`, `ReviewResumo` (Task 2); `IGameRepository.ObterPorId` (existente).
- Produces (contratos REST que a Task 5 verifica):
  - `PUT /api/jogos/{gameId}/avaliacoes` → `201` na criação, `200` na atualização, `400` nota fora de 1-5 ou corpo inválido, `404` jogo inexistente, `401` sem token.
  - `GET /api/jogos/{gameId}/avaliacoes` → `200` com lista (mais recentes primeiro), `404` jogo inexistente.
  - `GET /api/jogos/{gameId}/avaliacoes/resumo` → `200` com `{ jogoId, total, notaMedia }` (`notaMedia` nulo quando `total = 0`).

- [ ] **Step 1: Escrever os DTOs**

`CriarAvaliacaoDTO.cs`:

```csharp
namespace FCG.CatalogAPI.Application.DTOs;

/// <summary>
/// Entrada da avaliação. Não existe campo de usuário de propósito: o autor vem
/// sempre do claim "Id" do token, nunca do corpo da requisição.
/// </summary>
public class CriarAvaliacaoDTO
{
    public int Nota { get; set; }
    public string? Comentario { get; set; }
    public List<string>? Tags { get; set; }
}
```

`ReviewResponseDTO.cs`:

```csharp
namespace FCG.CatalogAPI.Application.DTOs;

public class ReviewResponseDTO
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public Guid UsuarioId { get; set; }
    public int Nota { get; set; }
    public string? Comentario { get; set; }
    public List<string> Tags { get; set; } = new();
    public DateTime DataCriacao { get; set; }
    public DateTime DataAtualizacao { get; set; }
}
```

`ReviewResumoDTO.cs`:

```csharp
namespace FCG.CatalogAPI.Application.DTOs;

public class ReviewResumoDTO
{
    public Guid JogoId { get; set; }
    public int Total { get; set; }
    public double? NotaMedia { get; set; }
}
```

- [ ] **Step 2: Escrever `ReviewService.cs`**

```csharp
using FCG.CatalogAPI.Application.DTOs;
using FCG.CatalogAPI.Domain.Entities;
using FCG.CatalogAPI.Domain.Interfaces;

namespace FCG.CatalogAPI.Application.Services;

public class ReviewService
{
    private readonly IReviewRepository _reviewRepository;
    private readonly IGameRepository _gameRepository;

    public ReviewService(IReviewRepository reviewRepository, IGameRepository gameRepository)
    {
        _reviewRepository = reviewRepository;
        _gameRepository = gameRepository;
    }

    public async Task<(ReviewResponseDTO Avaliacao, bool Criada)> AvaliarAsync(Guid gameId, Guid userId, CriarAvaliacaoDTO dto)
    {
        if (dto is null)
            throw new ArgumentException("Os dados da avaliação são obrigatórios.");

        if (dto.Nota < 1 || dto.Nota > 5)
            throw new ArgumentException("A nota deve estar entre 1 e 5.");

        // O jogo precisa existir no SQL: avaliação órfã não faz sentido.
        var jogo = await _gameRepository.ObterPorId(gameId);
        if (jogo is null)
            throw new KeyNotFoundException("Jogo não encontrado.");

        var agora = DateTime.UtcNow;
        var review = new Review
        {
            Id = Guid.NewGuid(),
            GameId = gameId,
            UserId = userId,
            Nota = dto.Nota,
            Comentario = string.IsNullOrWhiteSpace(dto.Comentario) ? null : dto.Comentario.Trim(),
            Tags = (dto.Tags ?? new List<string>())
                .Select(t => t?.Trim() ?? string.Empty)
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            DataCriacao = agora,
            DataAtualizacao = agora
        };

        var criada = await _reviewRepository.UpsertAsync(review);
        return (Mapear(review), criada);
    }

    public async Task<IEnumerable<ReviewResponseDTO>> ObterPorJogoAsync(Guid gameId)
    {
        var jogo = await _gameRepository.ObterPorId(gameId);
        if (jogo is null)
            throw new KeyNotFoundException("Jogo não encontrado.");

        var reviews = await _reviewRepository.ObterPorJogoAsync(gameId);
        return reviews.Select(Mapear).ToList();
    }

    public async Task<ReviewResumoDTO> ObterResumoAsync(Guid gameId)
    {
        var jogo = await _gameRepository.ObterPorId(gameId);
        if (jogo is null)
            throw new KeyNotFoundException("Jogo não encontrado.");

        var resumo = await _reviewRepository.ObterResumoAsync(gameId);
        return new ReviewResumoDTO
        {
            JogoId = gameId,
            Total = resumo.Total,
            NotaMedia = resumo.NotaMedia
        };
    }

    private static ReviewResponseDTO Mapear(Review review) => new()
    {
        Id = review.Id,
        GameId = review.GameId,
        UsuarioId = review.UserId,
        Nota = review.Nota,
        Comentario = review.Comentario,
        Tags = review.Tags,
        DataCriacao = review.DataCriacao,
        DataAtualizacao = review.DataAtualizacao
    };
}
```

- [ ] **Step 3: Escrever `AvaliacoesController.cs`**

```csharp
using FCG.CatalogAPI.Application.DTOs;
using FCG.CatalogAPI.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FCG.CatalogAPI.API.Controllers;

[ApiController]
[Route("api/jogos/{gameId:guid}/avaliacoes")]
public class AvaliacoesController : ControllerBase
{
    private readonly ReviewService _reviewService;

    public AvaliacoesController(ReviewService reviewService)
    {
        _reviewService = reviewService;
    }

    [HttpPut]
    [Authorize]
    public async Task<ActionResult<ReviewResponseDTO>> Avaliar(Guid gameId, [FromBody] CriarAvaliacaoDTO dto)
    {
        var userId = ObterUsuarioId();
        var (avaliacao, criada) = await _reviewService.AvaliarAsync(gameId, userId, dto);

        return criada ? Created(string.Empty, avaliacao) : Ok(avaliacao);
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IEnumerable<ReviewResponseDTO>>> ObterPorJogo(Guid gameId)
    {
        return Ok(await _reviewService.ObterPorJogoAsync(gameId));
    }

    [HttpGet("resumo")]
    [Authorize]
    public async Task<ActionResult<ReviewResumoDTO>> ObterResumo(Guid gameId)
    {
        return Ok(await _reviewService.ObterResumoAsync(gameId));
    }

    /// <summary>
    /// O autor da avaliação é o usuário do token (claim "Id", o mesmo usado pela
    /// users-api e pela biblioteca do monolito) — nunca um campo do corpo.
    /// </summary>
    private Guid ObterUsuarioId()
    {
        var claimId = User.FindFirst("Id")?.Value;
        if (claimId is null || !Guid.TryParse(claimId, out var userId))
            throw new UnauthorizedAccessException("O token não contém um identificador de usuário válido.");

        return userId;
    }
}
```

> Erros de negócio sobem como exceção e são formatados pelo `ErrorHandlingMiddleware` que já existe (`ArgumentException` → 400, `KeyNotFoundException` → 404, `UnauthorizedAccessException` → 401). O controller não repete `try/catch`.

- [ ] **Step 4: Registrar o serviço em `Program.cs`**

Junto dos demais serviços de aplicação:

```csharp
builder.Services.AddScoped<ReviewService>();
```

- [ ] **Step 5: Buildar com a tag do commit e aplicar (controlador)**

```powershell
$sha = (git -C .fase2-repos\fcg-catalog-api rev-parse --short HEAD).Substring(0,7)
Set-Location .fase2-repos\fcg-orchestration
docker build -t "fcg-catalog-api:sp3-$sha" ../fcg-catalog-api
# atualizar a imagem no manifesto para fcg-catalog-api:sp3-$sha e então:
kubectl apply -f k8s/catalog-api-deployment.yaml
kubectl rollout status deployment/catalog-api --timeout=300s
```

- [ ] **Step 6: Verificar os contratos pelo gateway (controlador)**

```powershell
kubectl port-forward svc/kong 18000:8000
# Precisa de um jogo existente: so o papel Admin cria jogo, e a users-api registra todo
# mundo como Usuario. Promova o jogador no SQL (a senha vem do Secret, nunca hardcoded),
# faca login de novo (o papel viaja no token) e crie o jogo para obter o gameId.
$sa = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String((kubectl get secret sqlserver-secret -o jsonpath='{.data.sa-password}')))
kubectl exec deploy/sqlserver -- /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $sa -C -Q "UPDATE FCG_Users.dbo.Users SET Role = 1 WHERE Email = 'jogador@fcg.com'"
Set-Content .superpowers\sdd\2026-09-14-sp3-nosql-cache\tmp\jogo.json -Value '{"nome":"Jogo SP3","descricao":"Criado na verificacao","preco":49.9}' -Encoding ascii -NoNewline
$gameId = (curl.exe -s -X POST -H "Authorization: Bearer $tokenAdmin" -H "Content-Type: application/json" -d '@.superpowers\sdd\2026-09-14-sp3-nosql-cache\tmp\jogo.json' http://localhost:18000/api/jogos | ConvertFrom-Json).id
# corpo por ARQUIVO: JSON inline perde as aspas no PowerShell e a API responde 400
Set-Content .superpowers\sdd\2026-09-14-sp3-nosql-cache\tmp\avaliacao.json -Value '{"nota":5,"comentario":"Muito bom","tags":["acao","top"]}' -Encoding ascii -NoNewline
Set-Content .superpowers\sdd\2026-09-14-sp3-nosql-cache\tmp\avaliacao2.json -Value '{"nota":3,"comentario":"Bom","tags":["acao"]}' -Encoding ascii -NoNewline
curl.exe -s -o NUL -w '%{http_code}' -X PUT -H "Authorization: Bearer $token" -H "Content-Type: application/json" -d '@...\avaliacao.json' "http://localhost:18000/api/jogos/$gameId/avaliacoes"   # 1a vez: 201
curl.exe -s -o NUL -w '%{http_code}' -X PUT -H "Authorization: Bearer $token" -H "Content-Type: application/json" -d '@...\avaliacao2.json' "http://localhost:18000/api/jogos/$gameId/avaliacoes" # 2a vez: 200
curl.exe -s -o NUL -w '%{http_code}' "http://localhost:18000/api/jogos/$gameId/avaliacoes" -H "Authorization: Bearer $token"          # 200
curl.exe -s "http://localhost:18000/api/jogos/$gameId/avaliacoes/resumo" -H "Authorization: Bearer $token"                             # {"jogoId":...,"total":1,"notaMedia":3}
curl.exe -s -o NUL -w '%{http_code}' -X PUT -H "Content-Type: application/json" -d '@...\avaliacao.json' "http://localhost:18000/api/jogos/$gameId/avaliacoes"  # sem token: 401
```

Também obrigatório: `{"nota":0,...}` e `{"nota":6,...}` → `400`; `gameId` inexistente → `404`; e **dois usuários diferentes** avaliando o mesmo jogo → `total: 2` no resumo (a prova de que o upsert é por par jogo/usuário, não global).

- [ ] **Step 7: Conferir o documento no banco (prova da persistência poliglota)**

```powershell
kubectl exec deploy/mongo -- mongosh -u fcg -p FcgMongo2026 --authenticationDatabase admin --quiet --eval "db.getSiblingDB('fcg_catalog').avaliacoes.find().pretty()"
```

Esperado: um documento por usuário/jogo, com `gameId`, `userId` (strings), `nota`, `comentario`, `tags`, `dataCriacao` e `dataAtualizacao`.

- [ ] **Step 8: Commit**

```bash
git add FCG.CatalogAPI.Application/DTOs/CriarAvaliacaoDTO.cs FCG.CatalogAPI.Application/DTOs/ReviewResponseDTO.cs FCG.CatalogAPI.Application/DTOs/ReviewResumoDTO.cs FCG.CatalogAPI.Application/Services/ReviewService.cs FCG.CatalogAPI.API/Controllers/AvaliacoesController.cs FCG.CatalogAPI.API/Program.cs
git commit -m "feat: endpoints de avaliacao de jogos com upsert por usuario e resumo agregado"
```

---

### Task 4: Cache de leitura do catálogo com degradação graciosa

**Files:**
- Create: `FCG.CatalogAPI.Infrastructure/Cache/GameCacheItem.cs`, `FCG.CatalogAPI.Infrastructure/Repositories/CachedGameRepository.cs`
- Modify: `FCG.CatalogAPI.Domain/Entities/Game.cs`, `FCG.CatalogAPI.Infrastructure/FCG.CatalogAPI.Infrastructure.csproj`, `FCG.CatalogAPI.API/Program.cs`, `k8s/catalog-api-configmap.yaml`, `k8s/catalog-api-deployment.yaml`

**Interfaces:**
- Consumes: `GameRepository` (concreto, existente), `IGameRepository` (existente), `Game` (existente), Service `redis` (Task 1).
- Produces: `cache_hit_total` e `cache_miss_total` no `/metrics`; chaves `catalog:games:all` e `catalog:game:{id}` com TTL de 60s; `IGameRepository` continua sendo a única abstração que o `GameService` vê.

- [ ] **Step 1: Adicionar os pacotes do cache e das métricas**

```xml
    <PackageReference Include="Microsoft.Extensions.Caching.StackExchangeRedis" Version="8.0.31" />
    <PackageReference Include="prometheus-net" Version="8.2.1" />
```

`prometheus-net` (o pacote core, não o `AspNetCore`) entra na **Infrastructure** porque os contadores `cache_hit`/`cache_miss` nascem no decorator, que é infraestrutura. A versão tem que ser **8.2.1**, a mesma que a API já usa via `prometheus-net.AspNetCore` 8.2.1: duas versões diferentes do core no mesmo processo brigam pelo registro da métrica.

- [ ] **Step 2: Construtor de reidratação em `Game.cs`**

```csharp
    /// <summary>
    /// Reidrata um jogo já persistido, preservando o Id original. É usado pelo cache
    /// (que guarda o objeto serializado); para criar um jogo novo continua valendo
    /// apenas o construtor de três parâmetros, que gera um Id.
    /// </summary>
    public Game(Guid id, string nome, string descricao, decimal preco, DateTime dataCadastro)
    {
        Id = id;
        Nome = nome;
        Descricao = descricao;
        Preco = preco;
        DataCadastro = dataCadastro;
        Usuarios = new List<UserGame>();
    }
```

- [ ] **Step 3: Escrever `GameCacheItem.cs`**

```csharp
using FCG.CatalogAPI.Domain.Entities;

namespace FCG.CatalogAPI.Infrastructure.Cache;

/// <summary>
/// Payload serializado no Redis. É um tipo próprio (e não a entidade Game) porque
/// a entidade tem setters privados e não pode ser desserializada por System.Text.Json.
/// </summary>
public class GameCacheItem
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public decimal Preco { get; set; }
    public DateTime DataCadastro { get; set; }

    public static GameCacheItem De(Game game) => new()
    {
        Id = game.Id,
        Nome = game.Nome,
        Descricao = game.Descricao,
        Preco = game.Preco,
        DataCadastro = game.DataCadastro
    };

    public Game ParaEntidade() => new(Id, Nome, Descricao, Preco, DataCadastro);
}
```

- [ ] **Step 4: Escrever `CachedGameRepository.cs`**

```csharp
using FCG.CatalogAPI.Domain.Entities;
using FCG.CatalogAPI.Domain.Interfaces;
using FCG.CatalogAPI.Infrastructure.Cache;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Prometheus;
using System.Text.Json;

namespace FCG.CatalogAPI.Infrastructure.Repositories;

/// <summary>
/// Decorator de cache-aside sobre o repositório do catálogo. A listagem sem paginação
/// é a consulta onerosa do projeto; o Redis evita repeti-la a cada chamada.
/// Qualquer falha do cache degrada para o SQL: o Redis nunca derruba a API.
/// </summary>
public class CachedGameRepository : IGameRepository
{
    private const string ChaveTodos = "catalog:games:all";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Counter Hits = Metrics.CreateCounter("cache_hit", "Leituras do catalogo atendidas pelo Redis.");
    private static readonly Counter Misses = Metrics.CreateCounter("cache_miss", "Leituras do catalogo que foram ao SQL Server.");

    private readonly IGameRepository _interno;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CachedGameRepository> _logger;
    private readonly List<Guid> _jogosAlterados = new();

    public CachedGameRepository(IGameRepository interno, IDistributedCache cache, ILogger<CachedGameRepository> logger)
    {
        _interno = interno;
        _cache = cache;
        _logger = logger;
    }

    private static string ChavePorId(Guid id) => $"catalog:game:{id}";

    public async Task<Game?> ObterPorId(Guid id)
    {
        var cacheado = await Ler<GameCacheItem>(ChavePorId(id));
        if (cacheado is not null)
            return cacheado.ParaEntidade();

        var game = await _interno.ObterPorId(id);
        if (game is not null)
            await Gravar(ChavePorId(id), GameCacheItem.De(game));

        return game;
    }

    public async Task<IEnumerable<Game>> ObterTodos()
    {
        var cacheado = await Ler<List<GameCacheItem>>(ChaveTodos);
        if (cacheado is not null)
            return cacheado.Select(i => i.ParaEntidade()).ToList();

        var games = (await _interno.ObterTodos()).ToList();
        await Gravar(ChaveTodos, games.Select(GameCacheItem.De).ToList());

        return games;
    }

    public async Task Adicionar(Game game)
    {
        await _interno.Adicionar(game);
        _jogosAlterados.Add(game.Id);
    }

    public async Task Remover(Game game)
    {
        await _interno.Remover(game);
        _jogosAlterados.Add(game.Id);
    }

    public async Task Salvar()
    {
        await _interno.Salvar();

        // Sem isto o POST/DELETE de jogo ficaria até 60s mentindo para quem lê.
        var chaves = new List<string> { ChaveTodos };
        chaves.AddRange(_jogosAlterados.Select(ChavePorId));
        _jogosAlterados.Clear();

        foreach (var chave in chaves)
            await RemoverChave(chave);
    }

    private async Task<T?> Ler<T>(string chave) where T : class
    {
        try
        {
            var json = await _cache.GetStringAsync(chave);
            if (string.IsNullOrEmpty(json))
            {
                Misses.Inc();
                return null;
            }

            var valor = JsonSerializer.Deserialize<T>(json, Json);
            if (valor is null)
            {
                Misses.Inc();
                return null;
            }

            Hits.Inc();
            return valor;
        }
        catch (Exception ex)
        {
            // Redis fora do ar (ou conteúdo corrompido): segue para o SQL.
            Misses.Inc();
            _logger.LogWarning(ex, "Falha ao ler a chave {Chave} do Redis; seguindo para o SQL Server.", chave);
            return null;
        }
    }

    private async Task Gravar<T>(string chave, T valor)
    {
        try
        {
            var json = JsonSerializer.Serialize(valor, Json);
            await _cache.SetStringAsync(chave, json, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = Ttl
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao gravar a chave {Chave} no Redis; a resposta segue sem cache.", chave);
        }
    }

    private async Task RemoverChave(string chave)
    {
        try
        {
            await _cache.RemoveAsync(chave);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao invalidar a chave {Chave} no Redis.", chave);
        }
    }
}
```

- [ ] **Step 5: Registrar o Redis e o decorator em `Program.cs`**

```csharp
var redisConnectionString = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";

builder.Services.AddStackExchangeRedisCache(options =>
{
    // abortConnect=false: sem Redis a API sobe e degrada em vez de falhar no boot.
    // Timeouts curtos: um Redis morto falha rápido, não pendura a requisição.
    options.ConfigurationOptions = new StackExchange.Redis.ConfigurationOptions
    {
        EndPoints = { redisConnectionString },
        AbortOnConnectFail = false,
        ConnectTimeout = 2000,
        SyncTimeout = 2000
    };
});
```

E trocar o registro do repositório do catálogo por (o `GameService` continua recebendo `IGameRepository`):

```csharp
builder.Services.AddScoped<GameRepository>();
builder.Services.AddScoped<IGameRepository>(sp => new CachedGameRepository(
    sp.GetRequiredService<GameRepository>(),
    sp.GetRequiredService<IDistributedCache>(),
    sp.GetRequiredService<ILogger<CachedGameRepository>>()));
```

`using` necessário:

```csharp
using FCG.CatalogAPI.Infrastructure.Cache;
using Microsoft.Extensions.Caching.Distributed;
```

- [ ] **Step 6: Configuração do cluster**

Em `k8s/catalog-api-configmap.yaml`, adicionar:

```yaml
  Redis__ConnectionString: "redis:6379"
```

Em `k8s/catalog-api-deployment.yaml`, atualizar a imagem para `fcg-catalog-api:sp3-<sha7>` deste commit.

- [ ] **Step 7: Buildar e aplicar (controlador)**

```powershell
$sha = (git -C .fase2-repos\fcg-catalog-api rev-parse --short HEAD).Substring(0,7)
Set-Location .fase2-repos\fcg-orchestration
docker build -t "fcg-catalog-api:sp3-$sha" ../fcg-catalog-api
kubectl apply -f k8s/catalog-api-configmap.yaml -f k8s/catalog-api-deployment.yaml
kubectl rollout status deployment/catalog-api --timeout=300s
```

- [ ] **Step 8: Verificar hit/miss, invalidação e degradação (controlador)**

```powershell
# 1) miss e depois hit na mesma chave (a imagem aspnet nao tem curl/wget: leia o /metrics por port-forward)
kubectl port-forward svc/catalog-api 18081:80
curl.exe -s -o NUL "http://localhost:18000/api/jogos" -H "Authorization: Bearer $token"   # miss
curl.exe -s -o NUL "http://localhost:18000/api/jogos" -H "Authorization: Bearer $token"   # hit
curl.exe -s http://localhost:18081/metrics | Select-String 'cache_(hit|miss)_total'
# esperado: cache_miss_total >= 1 e cache_hit_total >= 1
# 2) a segunda chamada é mais rápida (comparar Measure-Command das duas)
# 3) invalidação: POST /api/jogos com token de Admin deve zerar a chave catalog:games:all
kubectl exec deploy/redis -- redis-cli get catalog:games:all   # (nil) logo após o POST
# 4) degradação: com o Redis fora, a API continua respondendo
kubectl scale deployment/redis --replicas=0
curl.exe -s -o NUL -w '%{http_code}' "http://localhost:18000/api/jogos" -H "Authorization: Bearer $token"   # 200
kubectl logs deploy/catalog-api --tail=20 | Select-String 'Falha ao ler a chave'
kubectl scale deployment/redis --replicas=1
kubectl rollout status deployment/redis --timeout=120s
```

Para o passo 3 é preciso um usuário com papel **Admin** (só `[Authorize(Roles = "Admin")]` cria/apaga jogo, e a users-api registra todo mundo como `Usuario`). O caminho é promover um usuário no SQL antes do teste (`UserRole.Usuario = 0, UserRole.Admin = 1`; a senha do `sa` vem do Secret, nunca hardcoded):

```powershell
$sa = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String((kubectl get secret sqlserver-secret -o jsonpath='{.data.sa-password}')))
kubectl exec deploy/sqlserver -- /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $sa -C -Q "UPDATE FCG_Users.dbo.Users SET Role = 1 WHERE Email = 'jogador@fcg.com'"
```

Depois: `POST /api/jogos` (201) → `redis-cli get catalog:games:all` = `(nil)` → `GET /api/jogos` volta a ser miss. E o registro anterior do hit continua válido como evidência.

- [ ] **Step 9: Confirmar que o cache não quebrou a leitura do SQL (risco do novo construtor de `Game`)**

O EF Core materializa `Game` pelo construtor; com um segundo construtor público é preciso confirmar que a leitura continua correta:

```powershell
# com o cache FRIO (TTL expirado ou chave removida), a resposta vem do SQL Server:
kubectl exec deploy/redis -- redis-cli del catalog:games:all
curl.exe -s "http://localhost:18000/api/jogos" -H "Authorization: Bearer $token"
```

Esperado: a mesma lista de jogos de antes, com `id`, `nome`, `preco` e `dataCadastro` **preenchidos** (um `id` zerado ou `dataCadastro` nula significaria que o EF passou a usar o construtor errado — nesse caso o construtor de reidratação vira um factory estático com construtor privado, ou o cache passa a guardar o JSON do DTO).

- [ ] **Step 10: Commit**

```bash
git add FCG.CatalogAPI.Domain/Entities/Game.cs FCG.CatalogAPI.Infrastructure/Cache/GameCacheItem.cs FCG.CatalogAPI.Infrastructure/Repositories/CachedGameRepository.cs FCG.CatalogAPI.Infrastructure/FCG.CatalogAPI.Infrastructure.csproj FCG.CatalogAPI.API/Program.cs
git commit -m "feat: cache de leitura do catalogo em redis com degradacao graciosa"
```

---

### Task 5: README do orchestration e verificação final consolidada

**Files:**
- Modify: `fcg-orchestration/README.md`
- Create: `.superpowers/sdd/2026-09-14-sp3-nosql-cache/verify-sp3-final.ps1` (ledger, fora do git)

**Interfaces:**
- Consumes: tudo das Tasks 1-4.
- Produces: seção **Persistência poliglota e cache** no README, a evidência consolidada de aceite e a lista de follow-ups.

- [ ] **Step 1: Documentar a seção nova no README (antes de `## Estrutura de arquivos`)**

Conteúdo obrigatório (em pt-BR, no tom do resto do arquivo):

- **Por que MongoDB:** dados gerados pelos usuários (avaliações), schema flexível (comentário opcional e `tags[]` livre) e volumetria que cresce com o uso — o oposto do catálogo, que é relacional e pequeno. Driver oficial `MongoDB.Driver`. Coleção `avaliacoes`, com índice único `(gameId, userId)`.
- **Por que Redis:** a listagem do catálogo vai ao SQL inteira a cada chamada; o cache corta isso com TTL de **60s** e chaves `catalog:games:all` (prefixo `/api/jogos`) e `catalog:game:{id}`. Invalidação explícita em POST/DELETE de jogo.
- **Degradação graciosa:** com o Redis fora, a API responde igual (vai ao SQL) e loga `Falha ao ler a chave ...`. Com o **Mongo** fora, o catálogo continua funcionando — só os endpoints de avaliação falham.
- **Endpoints de avaliação:** `PUT/GET /api/jogos/{id}/avaliacoes` e `GET .../resumo`, com o autor vindo do claim `Id` do token (não do corpo), nota 1-5 e upsert por usuário/jogo (`201` criando, `200` atualizando).
- **Observabilidade:** `cache_hit_total`/`cache_miss_total` no `/metrics`, coletados pelo Prometheus do SP2.
- **Segredos:** comando de criação do `mongo-secret` (nunca o valor real).
- **Portas:** `mongo` (27017) e `redis` (6379) são `ClusterIP`, sem `port-forward` publicado no gateway; acesso local por `kubectl port-forward` quando precisar.
- **Limitação conhecida:** o `docker-compose.yml` (caminho sem Kubernetes) não sobe Mongo/Redis — apenas o fluxo do cluster contempla esta fase; registrado como follow-up.

- [ ] **Step 2: Escrever o verificador consolidado `verify-sp3-final.ps1` (ASCII puro)**

Estrutura (mesmo padrão dos scripts do SP2, que já funcionam neste ambiente e servem de referência literal: `.superpowers/sdd/2026-09-11-sp2-observabilidade/verify-sp2-final.ps1` — abra-o antes de escrever, para reaproveitar `Step`/`Code`/`Query`, o envio de corpo por arquivo, o `-Encoding Unicode` do log e o encerramento dos port-forwards):

```powershell
# Verificacao final do SP3 (MongoDB + Redis). ASCII puro.
param([int]$PromPort = 19096, [int]$GatewayPort = 18002)
# F1  pods e pvc: kubectl get pods; kubectl get pvc mongo-data
# F2  mongo e redis respondem: mongosh ping; redis-cli ping
# F3  cadastro + login (corpo por arquivo) + PUT/GET/GET resumo das avaliacoes
# F4  contratos negativos: nota 0 e 6 => 400; jogo inexistente => 404; sem token => 401
# F5  dois usuarios avaliam o mesmo jogo => resumo total=2
# F6  cache: limpar chave, 1a chamada (miss) e 2a (hit) com Measure-Command; contadores no /metrics
# F7  invalidação: POST /api/jogos com Admin => catalog:games:all ausente
# F8  resiliencia: scale redis 0 => GET /api/jogos 200 + log de degradacao; scale 1 => volta a cachear
# F9  persistencia: kubectl delete pod do mongo => avaliacoes continuam sendo lidas
# F10 port-forwards encerrados
```

- [ ] **Step 3: Rodar o verificador e registrar a evidência (controlador)**

```powershell
# Esta task nao muda codigo (so README), entao NAO ha rebuild: a imagem :sp3-<sha7> da Task 4
# continua sendo a que esta rodando. Se algum passo anterior tiver alterado C#, rebuild primeiro.
powershell -NoProfile -ExecutionPolicy Bypass -File .superpowers\sdd\2026-09-14-sp3-nosql-cache\verify-sp3-final.ps1 *> .superpowers\sdd\2026-09-14-sp3-nosql-cache\runtime-final.log
Get-Content .superpowers\sdd\2026-09-14-sp3-nosql-cache\runtime-final.log -Encoding Unicode
```

Evidência mínima exigida pela spec (§120): avaliação criada e lida, sobrevivendo ao `delete pod` do Mongo; `MongoDB.Driver` no `.csproj`; hit/miss visíveis e latência menor na segunda chamada; API funcional com o Redis derrubado.

- [ ] **Step 4: Conferir que nada do SP1/SP2 regrediu**

```powershell
kubectl get pods
kubectl port-forward svc/prometheus 19097:9090
curl.exe -s "http://localhost:19097/api/v1/targets?state=active"
# esperado: os alvos users-api:80 e catalog-api:80 continuam "up"; painel FCG - APIs intacto
curl.exe -s -o NUL -w '%{http_code}' http://localhost:18000/api/jogos   # sem token continua 401 (Kong)
```

- [ ] **Step 5: Commit**

```bash
git add README.md
git commit -m "docs: documenta a persistencia poliglota e o cache de leitura"
```

---

## Auto-review deste plano

- **Cobertura da spec (§113-120):** avaliações com `MongoDB.Driver` e documento com `gameId`/`userId`/`nota`/`comentario`/`tags[]`/datas ✔ (Task 2); regras "jogo existe (404), nota 1-5 (400), upsert por usuário/jogo (D9), `userId` do claim" ✔ (Task 3, com verificação no Step 6); cache com `Microsoft.Extensions.Caching.StackExchangeRedis`, chaves e TTL 60s, invalidação no POST/DELETE e contadores `cache_hit`/`cache_miss` ✔ (Task 4, com verificação nos Steps 8-9); indisponibilidade do Redis degrada para o SQL com log ✔ (Task 4 Step 8); PVC obrigatório para o Mongo ✔ (Task 1); verificação declarada na spec ✔ (Task 5).
- **Placeholders:** nenhum `TBD`/`TODO`. O único ponto em aberto deliberado é o plano B do construtor de `Game` (Task 4 Step 9), com o build/verificação de runtime como gate. A sobrecarga de agregação do driver (Task 2 Step 6) deixou de ser incerta: foi fixada na forma `PipelineDefinition<TInput, TOutput>.Create(IEnumerable<BsonDocument>, IBsonSerializer<TOutput>)`, confirmada na API oficial do driver.
- **Consistência de tipos:** `Review`/`ReviewResumo`/`IReviewRepository` definidos na Task 2 são usados com os mesmos nomes e assinaturas nas Tasks 3 e 4 (`UpsertAsync` retorna `bool` = criou; `ObterResumoAsync` retorna `ReviewResumo`); `GameCacheItem`/`CachedGameRepository` só aparecem na Task 4; `cache_hit`/`cache_miss` são exatamente os nomes do spec (o prometheus-net exporta como `cache_hit_total`/`cache_miss_total`).
- **Fora de escopo (D8), para não inflar:** paginação do catálogo, cache do resumo de avaliações, exclusão de avaliação própria, novas suítes de teste, refatorar os controllers existentes, Outbox, RS256, Mongo/Redis no `docker-compose.yml`.
- **Follow-ups que este plano cria (registrar ao fim):** corrigir `Detalhe = ex.StackTrace` no `ErrorHandlingMiddleware` do catalog-api (vaza stack trace ao cliente); premissa de 1 réplica no scrape do Prometheus quando as APIs escalarem; `docker-compose.yml` sem Mongo/Redis.
