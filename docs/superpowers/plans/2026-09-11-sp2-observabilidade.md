# SP2 — Observabilidade (Opção A: Prometheus + Grafana) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Instrumentar `users-api` e `catalog-api` com métricas Prometheus e health check, subir Prometheus + Grafana no cluster por manifestos e entregar dashboards de latência, throughput e erros — a decisão da stack registrada no README do repositório de orquestração.

**Architecture:** Cada API expõe `/metrics` (prometheus-net) e `/health`; o Prometheus raspa os dois Services (`users-api:80`, `catalog-api:80`) por ConfigMap, guardando dados em PVC; o Grafana lê do Prometheus com datasource e dashboards **provisionados por ConfigMap** (sem cliques manuais). Nada disso passa pelo Kong (as rotas do gateway cobrem só `/api/*`), então métricas e saúde não são expostas para fora do cluster.

**Tech Stack:** .NET 8 + `prometheus-net.AspNetCore` **8.2.1** (verificado no NuGet), `prom/prometheus:v3.1.0` e `grafana/grafana:11.4.0` (tags verificadas no registry), Kubernetes do cluster local (`kubectl` ativo, StorageClass default `standard` / `rancher.io/local-path`).

**Spec:** `docs/superpowers/specs/2026-09-11-fase3-gateway-serverless-observabilidade-design.md` (SP2; decisão D3 = Opção A)

## Global Constraints

- **Escopo:** apenas `users-api` e `catalog-api` (é o que a Opção A exige). `payments-api`/`notifications-api` **não** entram nesta rodada.
- **Namespace:** tudo em `default`; nenhum manifesto declara `namespace`.
- **Padrões do repositório:** nome do recurso = nome do serviço; label `app: <nome>` em `metadata.labels`, `template.metadata.labels` e `selector.matchLabels`; multi-documento no mesmo YAML separado por `---`.
- **Nunca definir porta HTTPS** nos serviços (`ASPNETCORE_HTTPS_PORTS`/`ASPNETCORE_URLS` com `https://`): o `UseHttpsRedirection()` passaria a responder 307 e o roteamento pelo Kong quebraria (o Kong não segue redirect).
- **`UseHttpMetrics()` logo após `builder.Build()`**, antes do `ErrorHandlingMiddleware`: assim as respostas de erro (401/404/500) também são contadas.
- **`/metrics` e `/health` não entram no Kong** — o gateway só roteia `/api/auth`, `/api/usuarios`, `/api/jogos`, `/api/biblioteca`.
- **Segredo do Grafana não vai para o git:** a senha do admin vive num `Secret` criado por comando documentado (`grafana-admin`), referenciado pelo Deployment.
- **Grafana e Prometheus são acessados por `port-forward`** (Services `ClusterIP`): neste cluster o `LoadBalancer` entrega um IP de rede não alcançável do host (lição do SP1).
- **Imagens locais:** após rebuild das APIs é obrigatório `kubectl rollout restart deploy/<serviço>` (`imagePullPolicy: IfNotPresent` reutilizaria a imagem antiga).
- **Sem testes automatizados** nos serviços (não há projeto de teste): a verificação é por execução real (`kubectl`/`curl`) e é executada pelo **controlador** — os implementers editam arquivos e commitam, sem rodar `kubectl`/`docker`.
- Branches: `fase3/sp2-observabilidade` em cada repositório tocado (`fcg-users-api`, `fcg-catalog-api`, `fcg-orchestration`).

---

## Mapa de arquivos

**Modificar em `fcg-users-api`:**
- `FCG.UsersAPI.API/FCG.UsersAPI.API.csproj` — adicionar `prometheus-net.AspNetCore` 8.2.1.
- `FCG.UsersAPI.API/Program.cs` — `AddHealthChecks()`, `UseHttpMetrics()`, `MapMetrics()`, `MapHealthChecks("/health")`.

**Modificar em `fcg-catalog-api`:**
- `FCG.CatalogAPI.API/FCG.CatalogAPI.API.csproj` — idem.
- `FCG.CatalogAPI.API/Program.cs` — idem.

**Modificar em `fcg-orchestration`:**
- `k8s/users-api-deployment.yaml` — `startupProbe`/`readinessProbe`/`livenessProbe` em `/health`.
- `k8s/catalog-api-deployment.yaml` — idem.
- `README.md` — seção `## Observabilidade`, tabela de serviços/portas e estrutura de arquivos.

**Criar em `fcg-orchestration`:**
- `k8s/prometheus-configmap.yaml` — `prometheus.yml` (scrape de `users-api:80` e `catalog-api:80` em `/metrics`).
- `k8s/prometheus-deployment.yaml` — `PersistentVolumeClaim` + `Deployment` + `Service` (`ClusterIP` 9090).
- `k8s/grafana-configmap.yaml` — `datasource.yml` + `dashboards.yml` (provisionamento).
- `k8s/grafana-dashboards-configmap.yaml` — `fcg-apis.json` (dashboard: latência p50/p95, throughput, requisições por status, taxa de erro).
- `k8s/grafana-deployment.yaml` — `Deployment` + `Service` (`ClusterIP` 3000), consumindo o `Secret` `grafana-admin`.

---

### Task 1: Instrumentar `users-api` (`/metrics` + `/health`) e probes

**Files:**
- Modify: `fcg-users-api/FCG.UsersAPI.API/FCG.UsersAPI.API.csproj` (bloco de PackageReference, linhas 9-17)
- Modify: `fcg-users-api/FCG.UsersAPI.API/Program.cs` (DI em :97-99; pipeline em :101-122)
- Modify: `fcg-orchestration/k8s/users-api-deployment.yaml` (Deployment, após `imagePullPolicy`/`ports`)

**Interfaces:**
- Produces: endpoint `GET /metrics` (formato Prometheus) e `GET /health` (200 `Healthy`) na porta 8080 do container — consumidos pelo Prometheus (Task 3) e pelas probes (este mesmo task).

- [ ] **Step 1: Adicionar o pacote**

Em `FCG.UsersAPI.API/FCG.UsersAPI.API.csproj`, dentro do `ItemGroup` de `PackageReference`, acrescentar:

```xml
    <PackageReference Include="prometheus-net.AspNetCore" Version="8.2.1" />
```

- [ ] **Step 2: Registrar health checks e métricas no DI**

Em `FCG.UsersAPI.API/Program.cs`, no bloco de DI (logo após `builder.Services.AddScoped<...>` que hoje termina na linha 99), acrescentar:

```csharp
builder.Services.AddHealthChecks();
```

- [ ] **Step 3: Instrumentar o pipeline**

Em `FCG.UsersAPI.API/Program.cs`, inserir `app.UseHttpMetrics();` **imediatamente após** `var app = builder.Build();` (linha 101) e **antes** de `using (var scope = app.Services.CreateScope())`, conforme o trecho final:

```csharp
var app = builder.Build();

// Metricas HTTP: registradas antes dos demais middlewares para contar tambem
// as respostas geradas por erro/autenticacao (401, 404, 500).
app.UseHttpMetrics();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<UsersDbContext>();
    db.Database.Migrate();
}

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "FCG Users API v1");
    c.ConfigObject.AdditionalItems["persistAuthorization"] = true;
});
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapMetrics();
app.MapHealthChecks("/health");

app.Run();
```

Observação: **não** definir `ASPNETCORE_HTTPS_PORTS`/`ASPNETCORE_URLS` com `https://` em nenhum manifesto.

- [ ] **Step 4: Adicionar as probes no manifesto**

Em `fcg-orchestration/k8s/users-api-deployment.yaml`, no container `users-api`, **depois** do bloco `ports:` (que tem `containerPort: 8080`), inserir:

```yaml
          startupProbe:
            httpGet:
              path: /health
              port: 8080
            periodSeconds: 5
            failureThreshold: 30
          readinessProbe:
            httpGet:
              path: /health
              port: 8080
            periodSeconds: 5
          livenessProbe:
            httpGet:
              path: /health
              port: 8080
            initialDelaySeconds: 30
            periodSeconds: 10
```

(`failureThreshold: 30` × 5s = 150s de tolerância: a aplicação roda `Database.Migrate()` no boot e precisa de folga.)

- [ ] **Step 5: Verificação de runtime (executada pelo controlador)**

Run (no repositório de orquestração):
```powershell
docker build -t fcg-users-api:latest ../fcg-users-api
kubectl apply -f k8s/users-api-deployment.yaml
kubectl rollout restart deployment/users-api
kubectl rollout status deployment/users-api --timeout=180s
kubectl get pods -l app=users-api
```
Expected: `pod/users-api-... condition met` e pod `1/1 Running`.
Depois, do host:
```powershell
kubectl port-forward svc/users-api 18080:80
# em outro terminal:
curl.exe -s -o NUL -w "%{http_code}" http://localhost:18080/health     # esperado 200
curl.exe -s http://localhost:18080/metrics | Select-String "http_requests_received_total"
```
Expected: `200` no `/health` e a série `http_requests_received_total` presente no `/metrics`. **Se o nome da métrica divergir** (a biblioteca pode variar entre versões), registre no relatório o nome real lido do `/metrics`: as queries do dashboard (Task 4) usam `http_requests_received_total` e `http_request_duration_seconds_bucket`.

- [ ] **Step 6: Commit**

```bash
# em fcg-users-api
git add FCG.UsersAPI.API/FCG.UsersAPI.API.csproj FCG.UsersAPI.API/Program.cs
git commit -m "feat: expoe metricas prometheus e health check"
# em fcg-orchestration
git add k8s/users-api-deployment.yaml
git commit -m "feat: adiciona probes de saude no users-api"
```

---

### Task 2: Instrumentar `catalog-api` (`/metrics` + `/health`) e probes

**Files:**
- Modify: `fcg-catalog-api/FCG.CatalogAPI.API/FCG.CatalogAPI.API.csproj`
- Modify: `fcg-catalog-api/FCG.CatalogAPI.API/Program.cs` (DI em :101-103; pipeline em :105-126)
- Modify: `fcg-orchestration/k8s/catalog-api-deployment.yaml`

**Interfaces:**
- Consumes: o mesmo padrão da Task 1 (não há dependência de código entre os dois serviços).
- Produces: `GET /metrics` e `GET /health` na porta 8080 do `catalog-api`.

- [ ] **Step 1: Adicionar o pacote**

Em `FCG.CatalogAPI.API/FCG.CatalogAPI.API.csproj`:

```xml
    <PackageReference Include="prometheus-net.AspNetCore" Version="8.2.1" />
```

- [ ] **Step 2: Registrar health checks**

Em `FCG.CatalogAPI.API/Program.cs`, no bloco de DI (após a linha 103), acrescentar:

```csharp
builder.Services.AddHealthChecks();
```

- [ ] **Step 3: Instrumentar o pipeline**

Em `FCG.CatalogAPI.API/Program.cs`, inserir `app.UseHttpMetrics();` imediatamente após `var app = builder.Build();` (linha 105), e antes de `app.Run();` acrescentar `app.MapMetrics();` e `app.MapHealthChecks("/health");` logo depois de `app.MapControllers();`:

```csharp
app.UseAuthorization();
app.MapControllers();
app.MapMetrics();
app.MapHealthChecks("/health");

app.Run();
```

- [ ] **Step 4: Adicionar as probes no manifesto**

Em `fcg-orchestration/k8s/catalog-api-deployment.yaml`, no container `catalog-api`, depois do bloco `ports:`:

```yaml
          startupProbe:
            httpGet:
              path: /health
              port: 8080
            periodSeconds: 5
            failureThreshold: 30
          readinessProbe:
            httpGet:
              path: /health
              port: 8080
            periodSeconds: 5
          livenessProbe:
            httpGet:
              path: /health
              port: 8080
            initialDelaySeconds: 30
            periodSeconds: 10
```

- [ ] **Step 5: Verificação de runtime (executada pelo controlador)**

```powershell
docker build -t fcg-catalog-api:latest ../fcg-catalog-api
kubectl apply -f k8s/catalog-api-deployment.yaml
kubectl rollout restart deployment/catalog-api
kubectl rollout status deployment/catalog-api --timeout=180s
kubectl port-forward svc/catalog-api 18081:80
# em outro terminal:
curl.exe -s -o NUL -w "%{http_code}" http://localhost:18081/health    # esperado 200
curl.exe -s http://localhost:18081/metrics | Select-String "http_request_duration_seconds"
```
Expected: `200` no `/health` e o histograma `http_request_duration_seconds` presente no `/metrics`.

- [ ] **Step 6: Commit**

```bash
# em fcg-catalog-api
git add FCG.CatalogAPI.API/FCG.CatalogAPI.API.csproj FCG.CatalogAPI.API/Program.cs
git commit -m "feat: expoe metricas prometheus e health check"
# em fcg-orchestration
git add k8s/catalog-api-deployment.yaml
git commit -m "feat: adiciona probes de saude no catalog-api"
```

---

### Task 3: Prometheus no cluster

**Files:**
- Create: `fcg-orchestration/k8s/prometheus-configmap.yaml`
- Create: `fcg-orchestration/k8s/prometheus-deployment.yaml`

**Interfaces:**
- Consumes: `/metrics` de `users-api:80` e `catalog-api:80` (Tasks 1 e 2).
- Produces: `Service/prometheus` (`ClusterIP`, porta 9090) — consumido pelo Grafana (Task 4) em `http://prometheus:9090`.

- [ ] **Step 1: Criar o ConfigMap de scrape**

Create `k8s/prometheus-configmap.yaml`:

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: prometheus-config
  labels:
    app: prometheus
data:
  prometheus.yml: |
    global:
      scrape_interval: 15s
      evaluation_interval: 15s

    scrape_configs:
      - job_name: prometheus
        static_configs:
          - targets: ["localhost:9090"]

      - job_name: fcg-apis
        metrics_path: /metrics
        static_configs:
          - targets:
              - users-api:80
              - catalog-api:80
```

(`users-api:80` e `catalog-api:80` são os nomes dos Services na porta 80, que encaminha para o 8080 do container.)

- [ ] **Step 2: Criar PVC + Deployment + Service**

Create `k8s/prometheus-deployment.yaml`:

```yaml
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: prometheus-data
  labels:
    app: prometheus
spec:
  accessModes:
    - ReadWriteOnce
  storageClassName: standard
  resources:
    requests:
      storage: 2Gi
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: prometheus
  labels:
    app: prometheus
spec:
  replicas: 1
  selector:
    matchLabels:
      app: prometheus
  template:
    metadata:
      labels:
        app: prometheus
    spec:
      securityContext:
        runAsUser: 65534
        runAsGroup: 65534
        fsGroup: 65534
      containers:
        - name: prometheus
          image: prom/prometheus:v3.1.0
          imagePullPolicy: IfNotPresent
          args:
            - --config.file=/etc/prometheus/prometheus.yml
            - --storage.tsdb.path=/prometheus
            - --storage.tsdb.retention.time=7d
          ports:
            - name: http
              containerPort: 9090
          readinessProbe:
            httpGet:
              path: /-/ready
              port: http
            initialDelaySeconds: 5
            periodSeconds: 5
          livenessProbe:
            httpGet:
              path: /-/healthy
              port: http
            initialDelaySeconds: 20
            periodSeconds: 10
          resources:
            limits:
              memory: 512Mi
              cpu: 500m
          volumeMounts:
            - name: config
              mountPath: /etc/prometheus
              readOnly: true
            - name: data
              mountPath: /prometheus
      volumes:
        - name: config
          configMap:
            name: prometheus-config
        - name: data
          persistentVolumeClaim:
            claimName: prometheus-data
---
apiVersion: v1
kind: Service
metadata:
  name: prometheus
  labels:
    app: prometheus
spec:
  type: ClusterIP
  selector:
    app: prometheus
  ports:
    - name: http
      port: 9090
      targetPort: http
```

- [ ] **Step 3: Aplicar e verificar (executada pelo controlador)**

```powershell
kubectl apply -f k8s/prometheus-configmap.yaml
kubectl apply -f k8s/prometheus-deployment.yaml
kubectl get pvc prometheus-data
kubectl rollout status deployment/prometheus --timeout=180s
kubectl port-forward svc/prometheus 19090:9090
# em outro terminal:
curl.exe -s http://localhost:19090/-/ready                              # esperado 200
curl.exe -s "http://localhost:19090/api/v1/targets?state=active" | Select-String "users-api|catalog-api"
curl.exe -s "http://localhost:19090/api/v1/query?query=http_requests_received_total" | Select-String "success"
```
Expected: PVC `Bound` (pode ficar `Pending` até o pod ser agendado — é o `WaitForFirstConsumer`), pod `1/1 Running`, `/‑/ready` = 200, **os dois alvos `users-api`/`catalog-api` com `"health":"up"`** e uma consulta retornando `"status":"success"` com séries. Se o pod falhar com `permission denied` em `/prometheus`, o PVC do `local-path` não aceitou o usuário 65534: remova o `securityContext.runAsUser` (mantendo `fsGroup`) e reaplique — registre no relatório.

- [ ] **Step 4: Commit**

```bash
git add k8s/prometheus-configmap.yaml k8s/prometheus-deployment.yaml
git commit -m "feat: adiciona prometheus com scrape das apis e volume persistente"
```

---

### Task 4: Grafana com datasource e dashboard provisionados

**Files:**
- Create: `fcg-orchestration/k8s/grafana-configmap.yaml`
- Create: `fcg-orchestration/k8s/grafana-dashboards-configmap.yaml`
- Create: `fcg-orchestration/k8s/grafana-deployment.yaml`

**Interfaces:**
- Consumes: `http://prometheus:9090` (Task 3).
- Produces: `Service/grafana` (`ClusterIP`, porta 3000) com datasource `Prometheus` e o dashboard **FCG — APIs** já provisionados.

- [ ] **Step 1: Criar o ConfigMap de provisionamento**

Create `k8s/grafana-configmap.yaml`:

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: grafana-provisioning
  labels:
    app: grafana
data:
  datasource.yml: |
    apiVersion: 1
    datasources:
      - name: Prometheus
        uid: prometheus
        type: prometheus
        access: proxy
        url: http://prometheus:9090
        isDefault: true
        editable: false
  dashboards.yml: |
    apiVersion: 1
    providers:
      - name: fcg
        orgId: 1
        folder: ""
        type: file
        disableDeletion: false
        updateIntervalSeconds: 30
        allowUiUpdates: true
        options:
          path: /var/lib/grafana/dashboards
```

- [ ] **Step 2: Criar o dashboard**

Create `k8s/grafana-dashboards-configmap.yaml`:

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: grafana-dashboards
  labels:
    app: grafana
data:
  fcg-apis.json: |
    {
      "uid": "fcg-apis",
      "title": "FCG - APIs",
      "tags": ["fcg", "observabilidade"],
      "timezone": "browser",
      "schemaVersion": 39,
      "version": 1,
      "refresh": "30s",
      "time": { "from": "now-30m", "to": "now" },
      "panels": [
        {
          "type": "timeseries",
          "title": "Latencia das requisicoes (p50 / p95)",
          "gridPos": { "h": 8, "w": 12, "x": 0, "y": 0 },
          "datasource": { "type": "prometheus", "uid": "prometheus" },
          "fieldConfig": { "defaults": { "unit": "s" }, "overrides": [] },
          "targets": [
            {
              "refId": "p95",
              "legendFormat": "p95 {{instance}}",
              "expr": "histogram_quantile(0.95, sum by (le, instance) (rate(http_request_duration_seconds_bucket[5m])))"
            },
            {
              "refId": "p50",
              "legendFormat": "p50 {{instance}}",
              "expr": "histogram_quantile(0.50, sum by (le, instance) (rate(http_request_duration_seconds_bucket[5m])))"
            }
          ]
        },
        {
          "type": "timeseries",
          "title": "Requisicoes por segundo",
          "gridPos": { "h": 8, "w": 12, "x": 12, "y": 0 },
          "datasource": { "type": "prometheus", "uid": "prometheus" },
          "fieldConfig": { "defaults": { "unit": "reqps" }, "overrides": [] },
          "targets": [
            {
              "refId": "rps",
              "legendFormat": "{{instance}}",
              "expr": "sum by (instance) (rate(http_requests_received_total[5m]))"
            }
          ]
        },
        {
          "type": "timeseries",
          "title": "Requisicoes por status code",
          "gridPos": { "h": 8, "w": 12, "x": 0, "y": 8 },
          "datasource": { "type": "prometheus", "uid": "prometheus" },
          "fieldConfig": { "defaults": { "unit": "reqps", "custom": { "stacking": { "mode": "normal" } } }, "overrides": [] },
          "targets": [
            {
              "refId": "status",
              "legendFormat": "{{code}}",
              "expr": "sum by (code) (rate(http_requests_received_total[5m]))"
            }
          ]
        },
        {
          "type": "timeseries",
          "title": "Taxa de erros (5xx / total)",
          "gridPos": { "h": 8, "w": 12, "x": 12, "y": 8 },
          "datasource": { "type": "prometheus", "uid": "prometheus" },
          "fieldConfig": { "defaults": { "unit": "percent", "min": 0 }, "overrides": [] },
          "targets": [
            {
              "refId": "erros",
              "legendFormat": "% 5xx",
              "expr": "100 * sum(rate(http_requests_received_total{code=~\"5..\"}[5m])) / clamp_min(sum(rate(http_requests_received_total[5m])), 1e-9)"
            }
          ]
        }
      ]
    }
```

Observação: o `uid` do datasource é resolvido pelo nome `Prometheus` na provisão; se o painel reclamar de datasource, troque `"uid": "prometheus"` por `"uid": "Prometheus"` — a verificação do Step 4 cobre isso.

- [ ] **Step 3: Criar Deployment + Service do Grafana**

Create `k8s/grafana-deployment.yaml`:

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: grafana
  labels:
    app: grafana
spec:
  replicas: 1
  selector:
    matchLabels:
      app: grafana
  template:
    metadata:
      labels:
        app: grafana
    spec:
      containers:
        - name: grafana
          image: grafana/grafana:11.4.0
          imagePullPolicy: IfNotPresent
          env:
            - name: GF_SECURITY_ADMIN_USER
              value: admin
            - name: GF_SECURITY_ADMIN_PASSWORD
              valueFrom:
                secretKeyRef:
                  name: grafana-admin
                  key: admin-password
            - name: GF_USERS_ALLOW_SIGN_UP
              value: "false"
          ports:
            - name: http
              containerPort: 3000
          readinessProbe:
            httpGet:
              path: /api/health
              port: http
            initialDelaySeconds: 10
            periodSeconds: 5
          livenessProbe:
            httpGet:
              path: /api/health
              port: http
            initialDelaySeconds: 30
            periodSeconds: 10
          resources:
            limits:
              memory: 512Mi
              cpu: 500m
          volumeMounts:
            - name: provisioning
              mountPath: /etc/grafana/provisioning
              readOnly: true
            - name: dashboards
              mountPath: /var/lib/grafana/dashboards
              readOnly: true
            - name: data
              mountPath: /var/lib/grafana
      volumes:
        - name: provisioning
          configMap:
            name: grafana-provisioning
            items:
              - key: datasource.yml
                path: datasources/datasource.yml
              - key: dashboards.yml
                path: dashboards/dashboards.yml
        - name: dashboards
          configMap:
            name: grafana-dashboards
        - name: data
          emptyDir: {}
---
apiVersion: v1
kind: Service
metadata:
  name: grafana
  labels:
    app: grafana
spec:
  type: ClusterIP
  selector:
    app: grafana
  ports:
    - name: http
      port: 3000
      targetPort: http
```

Nota: o `emptyDir` em `/var/lib/grafana` guarda o banco do Grafana (usuários, preferências); datasource e dashboards são recriados pela provisão a cada start — perder o banco não perde o dashboard.

- [ ] **Step 4: Aplicar e verificar (executada pelo controlador)**

O `Secret` é criado antes do apply (nunca no git):

```powershell
kubectl create secret generic grafana-admin --from-literal=admin-password='Fcg@Grafana2026' --dry-run=client -o yaml | kubectl apply -f -
kubectl apply -f k8s/grafana-configmap.yaml
kubectl apply -f k8s/grafana-dashboards-configmap.yaml
kubectl apply -f k8s/grafana-deployment.yaml
kubectl rollout status deployment/grafana --timeout=180s
kubectl port-forward svc/grafana 13000:3000
# em outro terminal:
curl.exe -s -o NUL -w "%{http_code}" http://localhost:13000/api/health          # esperado 200
curl.exe -s -u admin:Fcg@Grafana2026 http://localhost:13000/api/search?query=FCG | Select-String "FCG - APIs"
curl.exe -s -u admin:Fcg@Grafana2026 "http://localhost:13000/api/datasources" | Select-String "Prometheus"
```
Expected: `200` no health, o dashboard **FCG - APIs** presente na busca e o datasource `Prometheus` listado. Depois, no navegador (`http://localhost:13000`), os 4 painéis devem mostrar séries após gerar tráfego nas APIs (ex.: alguns `curl` no `/api/jogos` pelo Kong, que passam por users/catalog e são contados).

- [ ] **Step 5: Commit**

```bash
git add k8s/grafana-configmap.yaml k8s/grafana-dashboards-configmap.yaml k8s/grafana-deployment.yaml
git commit -m "feat: adiciona grafana com datasource e dashboard provisionados"
```

---

### Task 5: Documentação e verificação final do SP2

**Files:**
- Modify: `fcg-orchestration/README.md` (nova seção `## Observabilidade` antes de `## Estrutura de arquivos` (linha 371); tabela `### Serviços e portas` (linhas 58-62); bloco `## Estrutura de arquivos` (linhas 373-396))

- [ ] **Step 1: Documentar a escolha da stack (exigência do enunciado)**

Inserir, entre o fim da seção do gateway e `## Estrutura de arquivos`:

```markdown
## Observabilidade

A stack escolhida para esta fase é a **Opção A — Prometheus + Grafana** (código aberto, sem custo e sem dependência de conta em nuvem), implantada por manifestos Kubernetes neste repositório.

- **Instrumentação:** `users-api` e `catalog-api` expõem `/metrics` (biblioteca `prometheus-net`) e `/health`. O `UseHttpMetrics()` é registrado antes dos demais middlewares, então erros de autenticação e exceções também entram nas métricas.
- **Coleta:** o Prometheus raspa `users-api:80` e `catalog-api:80` a cada 15s (`k8s/prometheus-configmap.yaml`) e guarda 7 dias de dados em volume persistente (`prometheus-data`, 2Gi).
- **Visualização:** o Grafana sobe com datasource e dashboard **provisionados por ConfigMap** (`k8s/grafana-*.yaml`): o dashboard **FCG - APIs** traz latência (p50/p95), requisições por segundo, requisições por status code e taxa de erro 5xx.
- **Exposição:** nenhum dos dois é publicado pelo gateway — ambos são `ClusterIP` e o acesso é por `port-forward` (veja abaixo). O Kong só roteia `/api/*`, então `/metrics` e `/health` não saem do cluster.
- **Senha do Grafana:** o admin vem do `Secret` `grafana-admin` (nunca no git). Crie antes do apply:
  ```powershell
  kubectl create secret generic grafana-admin --from-literal=admin-password='<sua-senha>' --dry-run=client -o yaml | kubectl apply -f -
  ```
- **Acesso (dois terminais):**
  ```powershell
  kubectl port-forward svc/prometheus 19090:9090   # http://localhost:19090
  kubectl port-forward svc/grafana 13000:3000      # http://localhost:13000  (admin / senha do Secret)
  ```

> Para gerar tráfego e ver os painéis se movendo, use os exemplos de `### Acessar as APIs` (cadastro, login e chamadas autenticadas pelo gateway em `http://localhost:8000`).
```

- [ ] **Step 2: Atualizar a tabela de serviços/portas**

Em `### Serviços e portas`, acrescentar as linhas do Prometheus e do Grafana (com a observação de que são `ClusterIP` e o acesso é por `port-forward`), mantendo as entradas atuais de Kong, RabbitMQ e SQL Server.

- [ ] **Step 3: Atualizar a estrutura de arquivos**

No bloco `## Estrutura de arquivos`, acrescentar `prometheus-configmap.yaml`, `prometheus-deployment.yaml`, `grafana-configmap.yaml`, `grafana-dashboards-configmap.yaml` e `grafana-deployment.yaml`.

- [ ] **Step 4: Verificação final consolidada (executada pelo controlador)**

```powershell
kubectl get pods -l 'app in (users-api,catalog-api)'
kubectl get pods -l 'app in (prometheus,grafana)'
kubectl get pvc prometheus-data
kubectl get svc users-api catalog-api prometheus grafana
# alvos do Prometheus
kubectl port-forward svc/prometheus 19090:9090
curl.exe -s "http://localhost:19090/api/v1/targets?state=active" | Select-String '"health":"up"'
# consulta usada pelo painel de latência
curl.exe -s "http://localhost:19090/api/v1/query?query=histogram_quantile(0.95,%20sum%20by%20(le,%20instance)%20(rate(http_request_duration_seconds_bucket%5B5m%5D)))" | Select-String "success"
```
Expected: os 4 pods `Running`/`Ready`, PVC `Bound`, Services presentes, **dois alvos `up`** e a consulta de latência retornando dados após gerar tráfego.

- [ ] **Step 5: Commit**

```bash
git add README.md
git commit -m "docs: documenta a stack de observabilidade (prometheus e grafana)"
```

---

## Auto-review deste plano

- **Cobertura da spec (SP2):** instrumentar Users/Catalog com métricas no formato Prometheus ✔ (Tasks 1-2); dashboards com latência, contagem total e por status e taxa de erro ✔ (Task 4); implantação por manifestos Kubernetes ✔ (Tasks 3-4); escolha documentada no README do repositório de orquestração ✔ (Task 5); `/health` + probes como fundação ✔ (Tasks 1-2).
- **Placeholders:** nenhum — todos os YAMLs, o dashboard JSON e os comandos estão completos; os valores exatos (versões `8.2.1`, `v3.1.0`, `11.4.0`, portas, nomes de recursos) foram verificados no NuGet e no registry.
- **Consistência de nomes:** `users-api`/`catalog-api` (Services e labels `app:`), `prometheus`/`grafana`, `prometheus-config`/`prometheus-data`/`prometheus`, `grafana-provisioning`/`grafana-dashboards`/`grafana-admin` — usados igualmente nas Tasks 3, 4 e 5.
- **Riscos declarados:** PVC do `local-path` sem permissão para o usuário 65534 (fallback no Step 3 da Task 3); datasource uid no painel (nota no Step 2 da Task 4); `imagePullPolicy: IfNotPresent` exigindo `rollout restart` após rebuild (constraint global); Grafana/Prometheus acessíveis apenas por `port-forward` (constraint global).
