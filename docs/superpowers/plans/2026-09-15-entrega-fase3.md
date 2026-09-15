# Entrega da Fase 3 — Plano de Execução (vídeo + repositórios)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fechar os dois entregáveis da Fase 3 — o vídeo de até 10 minutos demonstrando a arquitetura em funcionamento e os cinco itens de código/repositório — sem reabrir o que já está pronto e mergeado.

**Architecture:** Nada de reescrita: o que falta é (a) instrumentar o único microsserviço que ficou sem métricas (`payments-api`), (b) centralizar os logs (Loki + Promtail) para que o log da função serverless apareça **na plataforma de observabilidade** e não só em `kubectl logs`, (c) transformar o README de orquestração no mapa explícito `requisito → onde está atendido`, e (d) um roteiro de gravação com comandos literais, tempos e um script de pré-checagem que impede gravar com a stack quebrada.

**Tech Stack:** Kong 3.9 (DB-less) · Azure Functions isolated worker .NET 8 + KEDA 2.20.2 · Prometheus 3.1.0 + Grafana 11.4.0 (**Opção A**) · **Loki + Promtail (novo)** · MongoDB 8.0.30 · Redis 7.4.11 · SQL Server 2022 · prometheus-net 8.2.1 · PowerShell 5.1.

**Spec:** `docs/superpowers/specs/2026-09-11-fase3-gateway-serverless-observabilidade-design.md` — e a lista de entregáveis do enunciado, transcrita em [Anexo A](#anexo-a--entregáveis-do-enunciado-transcritos).

## Global Constraints

- Namespace `default` para tudo; nenhuma porta HTTPS publicada em Service.
- **Nunca** `force-push`, **nunca** commit direto em `master`: branch + PR por repositório.
- **Nenhuma credencial em arquivo versionado** — Secrets nascem de `kubectl create secret` (ver seção *Segredos* do README).
- Toda rebuild de imagem exige **tag nova** (`:sp5-<sha7>`); `:latest` + `imagePullPolicy: IfNotPresent` reaproveita imagem velha do nó.
- Scripts `.ps1` em **ASCII sem BOM**; documentação em pt-BR.
- Métricas/campos exportados só entram em query de dashboard **depois de conferidos com `curl` no `/metrics`** (a lição do SP2: os contadores saíram como `cache_hit`, sem `_total`).
- O vídeo **não** pode mostrar segredo algum: nada de abrir `.env`, nenhum `kubectl get secret -o yaml`, nenhuma connection string na tela.

---

## Situação atual (auditoria com evidência)

| Entregável do enunciado | Estado | Evidência |
|---|---|---|
| Requisições via Gateway (roteamento + segurança) | ✅ pronto | Kong DB-less; `401` sem token, `200` com token; `k8s/kong/kong.yml.template` + `scripts/deploy-kong.ps1` |
| Função serverless acionada + **logs na plataforma centralizada** | ⚠️ **parcial** | a função é acionada e loga (`[EMAIL ENVIADO] Boas-vindas para ...`), mas **não há Loki/Promtail**: o log só aparece em `kubectl logs` |
| Observabilidade Opção A: dashboard Grafana em tempo real | ✅ pronto | dashboard `FCG - APIs` com 6 painéis; 2 alvos `up` |
| Explicar como o NoSQL foi integrado | ✅ pronto | Mongo (avaliações) + Redis (cache TTL 60s); seções *Persistência poliglota e cache* |
| Instrumentação nos repositórios dos microsserviços | ⚠️ **parcial** | `users-api` e `catalog-api` têm `prometheus-net.AspNetCore 8.2.1`; **`payments-api` não tem pacote, nem `/metrics`, nem alvo no scrape** |
| Link para o repositório da função serverless | ✅ pronto | tabela de arquitetura do README (linha 14) |
| Manifestos do Gateway + stack de monitoramento na orquestração | ✅ pronto | `k8s/kong/`, `k8s/prometheus-*`, `k8s/grafana-*` |
| Drivers de NoSQL e Cache | ✅ pronto | `MongoDB.Driver` + `StackExchange.Redis` no `catalog-api` (SP3) |
| README de orquestração como guia central | ⚠️ **parcial** | cobre a stack e como subir tudo, mas **sem o mapa `requisito → onde está atendido`** nem link de vídeo |

## Rulings deste plano

- **R1** — `payments-api` entra na observabilidade. Ele **não tem controllers** (é consumidor MassTransit puro: `Program.cs` termina em `app.Run()`), então `/metrics` de HTTP sozinho daria painel vazio; por isso a instrumentação inclui **métrica de negócio** (`pagamentos processados por status`), que produz sinal real quando uma compra acontece.
- **R2** — Logs centralizados com **Loki + Promtail** (não só `kubectl logs`): é o que torna o log da função visível *na plataforma* como o enunciado pede, e reaproveita o Grafana que já existe. Promtail está em fim de vida (sucessor: Grafana Alloy) — vai documentado nas limitações conhecidas.
- **R3** — Nada de reescrever o que já passou: SP1/SP2/SP3/SP4 ficam como estão; este trabalho é aditivo.
- **R4** — O vídeo é gravado por humano, mas **nenhum comando é improvisado**: tudo sai do roteiro, e um script de pré-checagem (`scripts/preflight-fase3.ps1`) valida a stack antes de apertar REC.

---

### Task 1: Instrumentar o `payments-api` (métricas HTTP + métricas de negócio)

**Files:**
- Modify: `.fase2-repos/fcg-payments-api/FCG.PaymentsAPI.API/FCG.PaymentsAPI.API.csproj`
- Modify: `.fase2-repos/fcg-payments-api/FCG.PaymentsAPI.Application/FCG.PaymentsAPI.Application.csproj`
- Modify: `.fase2-repos/fcg-payments-api/FCG.PaymentsAPI.API/Program.cs`
- Modify: `.fase2-repos/fcg-payments-api/FCG.PaymentsAPI.Application/Services/PaymentService.cs`
- Modify: `.fase2-repos/fcg-payments-api/README.md`
- Modify: `.fase2-repos/fcg-orchestration/k8s/prometheus-configmap.yaml`
- Modify: `.fase2-repos/fcg-orchestration/k8s/payments-api-deployment.yaml`
- Modify: `.fase2-repos/fcg-orchestration/k8s/grafana-dashboards-configmap.yaml`
- Modify: `.fase2-repos/fcg-orchestration/README.md`

**Interfaces:**
- Produces: métrica `fcg_payments_processados_total{status="Approved"|"Rejected"}` no `/metrics` do `payments-api:80`; alvo `payments-api:80` no job `fcg-apis`.
- Consumes: padrão do `users-api` (`using Prometheus;`, `app.UseHttpMetrics();`, `app.MapMetrics();`).

- [ ] **Step 1: Adicionar os pacotes**

Em `FCG.PaymentsAPI.API.csproj`, dentro do `<ItemGroup>` de `PackageReference` (junto de `Microsoft.OpenApi`):

```xml
    <PackageReference Include="prometheus-net.AspNetCore" Version="8.2.1" />
```

Em `FCG.PaymentsAPI.Application.csproj`, no `<ItemGroup>` de `PackageReference` (o contador vive na camada Application, como no `catalog-api`, onde o `prometheus-net` fica na Infrastructure):

```xml
    <PackageReference Include="prometheus-net" Version="8.2.1" />
```

- [ ] **Step 2: Registrar as métricas HTTP no `Program.cs`**

Trocar o `using` do topo e o bloco do `app`:

```csharp
using Prometheus;
```

```csharp
var app = builder.Build();

// Metricas HTTP: registradas antes dos demais middlewares (mesmo padrao do users-api).
app.UseHttpMetrics();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<PaymentsDbContext>();
    db.Database.Migrate();
}

app.MapMetrics();

app.Run();
```

- [ ] **Step 3: Criar o contador de negócio no `PaymentService`**

No topo de `PaymentService.cs`, adicionar o `using`:

```csharp
using Prometheus;
```

Dentro da classe, antes do construtor:

```csharp
    // Metrica de negocio: o payments-api e consumidor de fila, entao o /metrics HTTP
    // dele fica praticamente vazio -- e este contador que da sinal no dashboard.
    private static readonly Counter PagamentosProcessados = Metrics.CreateCounter(
        "fcg_payments_processados_total",
        "Pagamentos processados pela API, por status.",
        new CounterConfiguration { LabelNames = new[] { "status" } });
```

E no fim de `ProcessarPagamento`, logo depois do `_logger.LogInformation(...)` existente:

```csharp
        PagamentosProcessados.WithLabels(status).Inc();
```

- [ ] **Step 4: Compilar (validação obrigatória antes de qualquer imagem)**

Run:
```powershell
docker run --rm -v "C:\Users\Gustavo\fcg\.fase2-repos:/src" -v "nuget-cache:/root/.nuget/packages" -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet build /src/fcg-payments-api --nologo
```
Expected: `Build succeeded.` e `0 Error(s)` (o aviso `NU1903` do `Microsoft.OpenApi` é pré-existente).

- [ ] **Step 5: Rebuild com tag nova e subir**

Run:
```powershell
docker build -t fcg-payments-api:sp5-<sha7> C:\Users\Gustavo\fcg\.fase2-repos\fcg-payments-api
# trocar a imagem no manifesto
#   k8s/payments-api-deployment.yaml:  image: fcg-payments-api:sp5-<sha7>
kubectl apply -f k8s/payments-api-deployment.yaml
kubectl rollout status deployment/payments-api --timeout=180s
```
Expected: `deployment "payments-api" successfully rolled out` (`<sha7>` = `git -C .fase2-repos\fcg-payments-api rev-parse --short HEAD`).

- [ ] **Step 6: Expor o alvo no Prometheus e reiniciar**

Em `k8s/prometheus-configmap.yaml`, adicionar a terceira linha de alvos:

```yaml
      - job_name: fcg-apis
        metrics_path: /metrics
        static_configs:
          - targets:
              - users-api:80
              - catalog-api:80
              - payments-api:80
```

Run (o configmap é lido no boot do Prometheus — scrape novo exige restart):
```powershell
kubectl apply -f k8s/prometheus-configmap.yaml
kubectl rollout restart deployment/prometheus
kubectl rollout status deployment/prometheus --timeout=180s
```
Expected: 3 alvos `up` em `kubectl get --raw '/api/v1/namespaces/default/services/prometheus:9090/proxy/api/v1/targets'`.

- [ ] **Step 7: Conferir o nome REAL da métrica exportada (lição do SP2)**

Run:
```powershell
kubectl port-forward svc/payments-api 18081:80   # em outro terminal, durante o teste
curl.exe -s http://localhost:18081/metrics | Select-String 'fcg_payments'
```
Expected: a linha do contador, com o nome exatamente como o Prometheus o expõe. **Se o nome vier diferente do escrito no painel, corrigir a query do dashboard — nunca o contrário.**

- [ ] **Step 8: Painel no dashboard**

Adicionar ao dashboard `FCG - APIs` (em `k8s/grafana-dashboards-configmap.yaml`) um painel `timeseries`. **Os painéis existentes não têm campo `"id"`** (só `type`, `title`, `gridPos`, `datasource`, `fieldConfig`, `targets`) — siga o mesmo formato, e o `gridPos` livre na terceira linha do grid é `x: 12, y: 16`:

```json
        {
          "type": "timeseries",
          "title": "Pagamentos processados por status",
          "gridPos": { "h": 8, "w": 12, "x": 12, "y": 16 },
          "datasource": { "type": "prometheus", "uid": "prometheus" },
          "fieldConfig": { "defaults": { "unit": "short", "min": 0 }, "overrides": [] },
          "targets": [
            {
              "refId": "pagamentos",
              "legendFormat": "{{status}}",
              "expr": "sum by (status) (fcg_payments_processados_total)"
            }
          ]
        }
```

> A `expr` usa o nome **conferido no Step 7**. O `prometheus-net 8.2.1` exporta o nome exatamente como registrado (a lição do SP2: `cache_hit` saiu sem `_total`), então o contador foi nomeado já com o sufixo `_total`.

Run:
```powershell
kubectl apply -f k8s/grafana-dashboards-configmap.yaml
kubectl rollout restart deployment/grafana
kubectl rollout status deployment/grafana --timeout=180s
```
Expected: painel visível com duas séries (`Approved` / `Rejected`) após compras de teste.

- [ ] **Step 9: Prova de ponta a ponta (instrumentação com sinal real)**

Run:
```powershell
# login como admin -> token, depois 3 compras
curl.exe -s -o NUL -w '%{http_code}\n' -X POST http://localhost:8000/api/jogos/<gameId>/comprar -H "Authorization: Bearer <token>" -H "Content-Type: application/json" -d '{"userId":"<userId>","gameId":"<gameId>"}'
```
Expected: `202`; o contador `fcg_payments_processados_total` sobe e o painel mostra a série.

- [ ] **Step 10: Documentar e commitar**

Atualizar no README de orquestração a seção *Observabilidade*: **3 alvos** (users, catalog, payments) e o painel de pagamentos. No README do `payments-api`, registrar que o serviço é consumidor de fila e por isso expõe `/metrics` + contador de negócio.

```bash
cd .fase2-repos/fcg-payments-api
git checkout -b feat/observabilidade-metricas
git add -A
git commit -m "feat: expõe /metrics e contador de pagamentos processados (observabilidade)"
git push -u origin feat/observabilidade-metricas
gh pr create --base master --head feat/observabilidade-metricas --title "feat: instrumentação de observabilidade (métricas HTTP + de negócio)" --body-file <ledger>/pr-body-payments-obs.md
gh pr merge --merge
```

---

### Task 2: Logs centralizados — Loki + Promtail (log da função no Grafana)

**Files:**
- Create: `.fase2-repos/fcg-orchestration/k8s/loki-deployment.yaml`
- Create: `.fase2-repos/fcg-orchestration/k8s/promtail-deployment.yaml`
- Create: `.fase2-repos/fcg-orchestration/k8s/grafana-logs-configmap.yaml`
- Modify: `.fase2-repos/fcg-orchestration/k8s/grafana-configmap.yaml`
- Modify: `.fase2-repos/fcg-orchestration/README.md`

**Interfaces:**
- Produces: Loki em `http://loki:3100` (Service `loki`), datasource Grafana `Loki` (uid `loki`), dashboard `FCG - Logs (Loki)` com o log da função.
- Consumes: ConfigMaps de provisionamento `grafana-provisioning` (datasource) e provider de arquivos em `/var/lib/grafana/dashboards`.

- [ ] **Step 1: Confirmar as tags das imagens ANTES de escrever o manifesto**

Run:
```powershell
docker pull grafana/loki:3.4.2
docker pull grafana/promtail:3.4.2
```
Expected: `Status: Downloaded newer image` (ou `Image is up to date`). Se a tag não existir, usar a série 3.x mais recente disponível e **manter Loki e Promtail na mesma série** (a lição do `kong:3.10`, que não existe).

- [ ] **Step 2: Criar o `loki-deployment.yaml`**

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: loki
  labels:
    app: loki
spec:
  replicas: 1
  selector:
    matchLabels:
      app: loki
  template:
    metadata:
      labels:
        app: loki
    spec:
      containers:
        - name: loki
          image: grafana/loki:3.4.2
          imagePullPolicy: IfNotPresent
          args: ["-config.file=/etc/loki/loki.yml"]
          ports:
            - name: http
              containerPort: 3100
          volumeMounts:
            - name: config
              mountPath: /etc/loki
            - name: data
              mountPath: /loki
          readinessProbe:
            httpGet:
              path: /ready
              port: http
            initialDelaySeconds: 15
            periodSeconds: 10
          livenessProbe:
            httpGet:
              path: /ready
              port: http
            initialDelaySeconds: 45
            periodSeconds: 20
      volumes:
        - name: config
          configMap:
            name: loki-config
        - name: data
          emptyDir: {}
---
apiVersion: v1
kind: ConfigMap
metadata:
  name: loki-config
  labels:
    app: loki
data:
  loki.yml: |
    auth_enabled: false
    server:
      http_listen_port: 3100
    common:
      instance_addr: 127.0.0.1
      path_prefix: /loki
      storage:
        filesystem:
          chunks_directory: /loki/chunks
          rules_directory: /loki/rules
      replication_factor: 1
      ring:
        kvstore:
          store: inmemory
    schema_config:
      configs:
        - from: 2024-01-01
          store: tsdb
          object_store: filesystem
          schema: v13
          index:
            prefix: index_
            period: 24h
    limits_config:
      retention_period: 24h
      allow_structured_metadata: true
    compactor:
      working_directory: /loki/compactor
      retention_enabled: true
      delete_request_store: filesystem
---
apiVersion: v1
kind: Service
metadata:
  name: loki
  labels:
    app: loki
spec:
  type: ClusterIP
  selector:
    app: loki
  ports:
    - port: 3100
      targetPort: http
```

> Sem PVC de propósito (como o Redis): o volume é `emptyDir` e a retenção é de 24h — o suficiente para a demonstração, e está registrado nas limitações conhecidas.

- [ ] **Step 3: Criar o `promtail-deployment.yaml`** (DaemonSet + RBAC + config que coleta **só** a stack FCG)

```yaml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: promtail
  labels:
    app: promtail
---
apiVersion: rbac.authorization.k8s.io/v1
kind: ClusterRole
metadata:
  name: promtail
rules:
  - apiGroups: [""]
    resources: ["nodes", "nodes/proxy", "services", "endpoints", "pods"]
    verbs: ["get", "list", "watch"]
---
apiVersion: rbac.authorization.k8s.io/v1
kind: ClusterRoleBinding
metadata:
  name: promtail
roleRef:
  apiGroup: rbac.authorization.k8s.io
  kind: ClusterRole
  name: promtail
subjects:
  - kind: ServiceAccount
    name: promtail
    namespace: default
---
apiVersion: v1
kind: ConfigMap
metadata:
  name: promtail-config
  labels:
    app: promtail
data:
  promtail.yml: |
    server:
      http_listen_port: 9080
      grpc_listen_port: 0
    positions:
      filename: /run/promtail/positions.yaml
    clients:
      - url: http://loki:3100/loki/api/v1/push
    scrape_configs:
      - job_name: fcg-pods
        kubernetes_sd_configs:
          - role: pod
        relabel_configs:
          # so a stack FCG entra no Loki (o volume de log do cluster inteiro nao ajuda a demo)
          - source_labels: [__meta_kubernetes_pod_label_app]
            regex: "notifications-function|users-api|catalog-api|payments-api|kong"
            action: keep
          - source_labels: [__meta_kubernetes_pod_label_app]
            target_label: app
          - source_labels: [__meta_kubernetes_pod_name]
            target_label: pod
          - source_labels: [__meta_kubernetes_namespace]
            target_label: namespace
---
apiVersion: apps/v1
kind: DaemonSet
metadata:
  name: promtail
  labels:
    app: promtail
spec:
  selector:
    matchLabels:
      app: promtail
  template:
    metadata:
      labels:
        app: promtail
    spec:
      serviceAccountName: promtail
      containers:
        - name: promtail
          image: grafana/promtail:3.4.2
          imagePullPolicy: IfNotPresent
          args: ["-config.file=/etc/promtail/promtail.yml"]
          volumeMounts:
            - name: config
              mountPath: /etc/promtail
            - name: run
              mountPath: /run/promtail
            - name: pods
              mountPath: /var/log/pods
              readOnly: true
      volumes:
        - name: config
          configMap:
            name: promtail-config
        - name: run
          hostPath:
            path: /run/promtail
        - name: pods
          hostPath:
            path: /var/log/pods
```

- [ ] **Step 4: Datasource do Loki no Grafana**

Em `k8s/grafana-configmap.yaml`, adicionar a segunda entrada em `datasource.yml`:

```yaml
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
      - name: Loki
        uid: loki
        type: loki
        access: proxy
        url: http://loki:3100
        editable: false
```

- [ ] **Step 5: Dashboard de logs**

Criar `k8s/grafana-logs-configmap.yaml`:

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: grafana-logs-dashboard
  labels:
    app: grafana
data:
  fcg-logs.json: |
    {
      "title": "FCG - Logs (Loki)",
      "uid": "fcg-logs",
      "schemaVersion": 39,
      "refresh": "5s",
      "time": { "from": "now-15m", "to": "now" },
      "panels": [
        {
          "id": 1,
          "type": "logs",
          "title": "Funcao de notificacoes (notifications-function)",
          "datasource": { "type": "loki", "uid": "loki" },
          "gridPos": { "h": 12, "w": 24, "x": 0, "y": 0 },
          "targets": [ { "refId": "A", "expr": "{app=\"notifications-function\"}" } ],
          "options": { "showTime": true, "wrapLogMessage": true, "sortOrder": "Descending", "enableLogDetails": true }
        },
        {
          "id": 2,
          "type": "logs",
          "title": "APIs (users / catalog / payments) e Gateway",
          "datasource": { "type": "loki", "uid": "loki" },
          "gridPos": { "h": 12, "w": 24, "x": 0, "y": 12 },
          "targets": [ { "refId": "A", "expr": "{app=~\"users-api|catalog-api|payments-api|kong\"}" } ],
          "options": { "showTime": true, "wrapLogMessage": true, "sortOrder": "Descending", "enableLogDetails": true }
        }
      ]
    }
```

O provider de dashboards já aponta para `/var/lib/grafana/dashboards`, então basta montar este ConfigMap no mesmo diretório do `grafana-dashboards-configmap.yaml` (conferir o `volumes`/`volumeMounts` do `k8s/grafana-deployment.yaml` e adicionar o novo `configMap` na lista).

- [ ] **Step 6: Aplicar e verificar**

Run:
```powershell
kubectl apply -f k8s/loki-deployment.yaml -f k8s/promtail-deployment.yaml -f k8s/grafana-configmap.yaml -f k8s/grafana-logs-configmap.yaml
kubectl apply -f k8s/grafana-deployment.yaml
kubectl rollout status deployment/loki --timeout=180s
kubectl rollout status deployment/grafana --timeout=180s
kubectl rollout status daemonset/promtail --timeout=180s
```
Expected: pods `loki`, `promtail` e `grafana` `1/1 Running`.

- [ ] **Step 7: Provar que o log da função chega ao Loki (o ponto do entregável)**

Run:
```powershell
kubectl port-forward svc/loki 13100:3100   # outro terminal
# consulta direta no Loki (a "plataforma centralizada"):
curl.exe -s -G http://localhost:13100/loki/api/v1/query_range --data-urlencode 'query={app="notifications-function"}' --data-urlencode 'limit=20'
```
Expected: resposta JSON do Loki com as linhas de log da função (o `[EMAIL ENVIADO]` aparece **depois** de um cadastro, e o pod da função pode estar em 0 réplicas entre eventos — sem evento novo, a consulta volta vazia, e isso é o comportamento correto).

- [ ] **Step 8: Documentar e commitar**

README de orquestração: subseção **Logs (Loki)** dentro de *Observabilidade* — por que Loki (o enunciado pede os logs da função *na plataforma*), como consultar (`kubectl port-forward svc/loki 13100:3100` e `curl`/Grafana Explore), o `emptyDir` sem PVC e o fim de vida do Promtail (sucessor: Grafana Alloy).

```bash
cd .fase2-repos/fcg-orchestration
git checkout -b feat/logs-loki
git add -A && git commit -m "feat: logs centralizados com Loki + Promtail e dashboard de logs no Grafana"
git push -u origin feat/logs-loki
gh pr create --base master --head feat/logs-loki --title "feat: logs centralizados (Loki + Promtail) para a demonstração da função serverless" --body-file <ledger>/pr-body-loki.md
gh pr merge --merge
```

---

### Task 3: README como guia central — mapa `requisito → onde está atendido`

**Files:**
- Modify: `.fase2-repos/fcg-orchestration/README.md` (inserir a seção logo depois de *Arquitetura*, antes de *Como executar com Docker*)

**Interfaces:**
- Produces: tabela de rastreabilidade que a banca usa para conferir o enunciado; âncora `#atendimento-dos-requisitos-da-fase-3`.

- [ ] **Step 1: Inserir a seção**

```markdown
## Atendimento dos requisitos da Fase 3

Cada requisito da fase, onde ele está implementado e o comando que comprova — sem precisar navegar pelo repositório. A demonstração em vídeo segue a mesma ordem.

| Requisito | Onde está | Como comprovar |
|---|---|---|
| **API Gateway** | `k8s/kong/kong-deployment.yaml`, `k8s/kong/kong.yml.template`, `scripts/deploy-kong.ps1` | `curl.exe -i http://localhost:8000/api/jogos` → `401`; com token → `200` (ver [API Gateway](#api-gateway-kong)) |
| **Função serverless (escala a zero)** | repositório [fcg-notifications-function](https://github.com/gustavoaa-dev/fcg-notifications-function), implantada por Terraform + KEDA (`k8s/keda/README.md`) | `kubectl get pods -l app=notifications-function` → nenhum pod; um cadastro faz o pod subir (~15 s) e o log aparecer (ver [Serverless](#serverless-função-de-notificações)) |
| **Observabilidade (Opção A: Prometheus + Grafana)** | `k8s/prometheus-*.yaml`, `k8s/grafana-*.yaml`, `k8s/loki-deployment.yaml` | dashboard `FCG - APIs` em tempo real e logs em `FCG - Logs (Loki)` (ver [Observabilidade](#observabilidade)) |
| **Persistência poliglota (NoSQL)** | `fcg-catalog-api` (`MongoDB.Driver`, `ReviewDocument`, `MongoReviewRepository`) | `PUT`/`GET /api/jogos/{id}/avaliacoes` (ver [Persistência poliglota e cache](#persistência-poliglota-e-cache)) |
| **Cache distribuído (Redis)** | `fcg-catalog-api` (`CachedGameRepository`, `catalog:games:all`, `catalog:game:{id}`, TTL 60 s) | `kubectl exec deploy/redis -- redis-cli keys 'catalog:*'` e os contadores `cache_hit`/`cache_miss` no `/metrics` |
| **Instrumentação nos microsserviços** | `prometheus-net` em `users-api`, `catalog-api` e `payments-api` | `kubectl port-forward svc/<api> 8080:80` + `curl.exe localhost:8080/metrics` |
| **Segredos fora do repositório** | `.gitignore`, `.env.example`, seção [Segredos](#segredos) | `git grep -n 'FCG@Password123\|Fcg2024Test!'` → vazio |

**Demonstração em vídeo:** *[link do vídeo]* — roteiro em [`docs/roteiro-video-fase3.md`](docs/roteiro-video-fase3.md).
```

- [ ] **Step 2: Verificar que todo link da tabela resolve**

Run:
```powershell
Select-String -Path README.md -Pattern '^\| \*\*' | Measure-Object   # 7 linhas de requisito
# e o link do video:
Select-String -Path README.md -Pattern 'roteiro-video-fase3'
```
Expected: 7 linhas na tabela e o caminho do roteiro existindo (`Test-Path docs/roteiro-video-fase3.md` → `True`, criado na Task 4).

- [ ] **Step 3: Commit**

```bash
git checkout -b docs/mapa-requisitos-fase3
git add README.md
git commit -m "docs: mapa requisito -> onde esta atendido (guia central da entrega)"
git push -u origin docs/mapa-requisitos-fase3
gh pr create --base master --head docs/mapa-requisitos-fase3 --title "docs: README como guia central da Fase 3 (mapa de requisitos)" --body-file <ledger>/pr-body-mapa.md
gh pr merge --merge
```

---

### Task 4: Roteiro do vídeo + scripts de apoio (preflight e tráfego)

**Files:**
- Create: `.fase2-repos/fcg-orchestration/docs/roteiro-video-fase3.md`
- Create: `.fase2-repos/fcg-orchestration/scripts/preflight-fase3.ps1`
- Create: `.fase2-repos/fcg-orchestration/scripts/demo-trafego.ps1`

**Interfaces:**
- Produces: `scripts/preflight-fase3.ps1` (sai com `exit 1` se algo estiver fora do ar) e `scripts/demo-trafego.ps1 -Segundos 60` (gera tráfego autenticado para o dashboard se mexer).
- Consumes: mesma convenção dos scripts existentes (ASCII sem BOM, `Chk` acumulando falhas, `exit 1`).

- [ ] **Step 1: Escrever o roteiro (conteúdo integral)**

O arquivo `docs/roteiro-video-fase3.md` deve conter, no mínimo, o roteiro abaixo — **tempos, comando literal e o que deve aparecer na tela**, na ordem:

| Tempo | Bloco | Na tela |
|---|---|---|
| 0:00–0:45 | **Abertura e arquitetura** | README aberto na seção *Arquitetura* + diagrama de fluxo de eventos; 1 frase por componente (Kong, 3 APIs, função, Mongo, Redis, Prometheus/Grafana/Loki) |
| 0:45–3:00 | **Gateway: roteamento e segurança** | `kubectl get svc kong` (Service do gateway) → `curl.exe -i http://localhost:8000/api/jogos` (**401**) → `curl.exe -X POST http://localhost:8000/api/auth/login -d '{...}'` (**200** + token) → `curl.exe -H "Authorization: Bearer <token>" .../api/jogos` (**200**) → `kubectl port-forward svc/kong 8001:8001` + `curl.exe localhost:8001/routes` (mostra as rotas) e a config DB-less com o plugin `jwt`; fechar mostrando que `svc/users-api` é `ClusterIP` (API não exposta) |
| 3:00–5:15 | **Função serverless + log centralizado** | `kubectl get deploy notifications-function` (**0/0**) → `kubectl get pods -l app=notifications-function -w` em um terminal → cadastro pelo gateway (`201`) → **pod sobe em ~15 s** → Grafana `FCG - Logs (Loki)` com `[EMAIL ENVIADO] Boas-vindas para ...` (o log aparece **na plataforma**, não no terminal) → pod volta a zero |
| 5:15–7:30 | **Observabilidade (Opção A)** | `scripts/demo-trafego.ps1 -Segundos 90` rodando em um terminal + dashboard `FCG - APIs` em tela cheia (latência p50/p95, RPS, status code, erros, `up`) → Prometheus `Status → Targets` com **3 alvos `up`** → painel *Pagamentos processados por status* mexendo após uma compra |
| 7:30–9:15 | **NoSQL na arquitetura** | `PUT /api/jogos/{id}/avaliacoes` (upsert devolve o documento persistido) → `GET /api/jogos/{id}/avaliacoes` (lista vinda do Mongo) → `kubectl exec deploy/redis -- redis-cli keys 'catalog:*'` + `type`/`ttl` → contadores `cache_hit`/`cache_miss`; explicar **por que** Mongo (documento flexível de avaliação) e **por que** Redis (cache de leitura com TTL 60 s), e que o SQL continua dono do dado transacional |
| 9:15–10:00 | **Repositórios e fechamento** | tabela de repositórios do README (5 repos, incluindo o link da função) + a seção *Atendimento dos requisitos da Fase 3*; fechar com "como subir tudo": `kubectl create secret ...` → `kubectl apply -f k8s/` → `scripts/deploy-kong.ps1` |

Regras do roteiro: (a) **nunca** abrir `.env` nem `kubectl get secret -o yaml`; (b) cada comando já testado no preflight; (c) se o pod da função demorar, narração cobre a espera — os ~15 s **são** a prova da escala a zero; (d) se estourar o tempo, cortar primeiro o `port-forward` da Admin API do Kong; (e) o par usuário/senha de demonstração é fixo e criado pelo preflight **antes** da gravação (o bloco do Gateway faz login no começo do vídeo, e o cadastro do bloco serverless usa um e-mail novo, que é o que acorda a função).

- [ ] **Step 2: Escrever o `preflight-fase3.ps1`**

Checagens (cada uma incrementa `$falhas` quando falha, como no `verify-sp4-final.ps1`):

```powershell
# 1) 11 pods Running/Ready (sqlserver, rabbitmq, mongo, redis, users-api, catalog-api,
#    payments-api, kong, prometheus, grafana, loki) + promtail Running no DaemonSet
# 2) gateway responde 401 sem token em /api/jogos
# 3) login devolve token (200) e GET /api/jogos com o token devolve 200
# 4) Prometheus: 3 alvos up (users-api, catalog-api, payments-api)
# 5) Grafana: /api/health = ok e o dashboard fcg-logs provisionado (GET /api/search)
# 6) Loki: /ready = ready (via kubectl get --raw .../services/loki:3100/proxy/ready)
# 7) funcao em 0 replicas (estado inicial da demo) e ScaledObject Ready=True
# 8) Redis com as chaves catalog:* e Mongo respondendo (GET .../avaliacoes = 200)
# 9) USUARIO DE DEMONSTRACAO: login com o par email/senha da demo devolve 200; se nao
#    existir, o preflight CRIA pelo gateway (POST /api/usuarios) e avisa -- o bloco do
#    Gateway faz login ANTES do bloco de cadastro, entao o usuario precisa existir antes
#    de apertar REC. Usar um par fixo e documentado no roteiro (ex.: demo@fcg.local).
```

Saída final: `TUDO PRONTO PARA GRAVAR` ou a lista de falhas + `exit 1`.

- [ ] **Step 3: Escrever o `demo-trafego.ps1`**

```powershell
param(
  [int]$Segundos = 60,
  [string]$Gateway = 'http://localhost:8000',
  [string]$Email = 'demo@fcg.local',
  # SEM default: a senha da demonstracao NAO pode ficar versionada (regra global de segredos).
  # Vem por parametro ou da variavel de ambiente FCG_DEMO_SENHA.
  [string]$Senha = $env:FCG_DEMO_SENHA
)
# login -> token; enquanto o cronometro nao zera: GET /api/jogos, GET /api/jogos/{id},
# GET /api/jogos/{id}/avaliacoes, com pausa de 200 ms entre chamadas; imprime o contador de requisicoes.
# Se $Senha vier vazia, abortar com mensagem clara (nada de senha default no arquivo).
```

Requisito de aceite: com o dashboard aberto, as séries de RPS/latência **se movem** em até 15 s (o scrape é de 15 s).

- [ ] **Step 4: Rodar o preflight e ajustar o que falhar**

Run:
```powershell
powershell -ExecutionPolicy Bypass -File scripts/preflight-fase3.ps1
```
Expected: `TUDO PRONTO PARA GRAVAR` (com a stack da Task 1 e Task 2 aplicada). Qualquer falha aqui é corrigida **antes** de gravar.

- [ ] **Step 5: Commit**

```bash
git checkout -b docs/roteiro-e-preflight-video
git add docs/roteiro-video-fase3.md scripts/preflight-fase3.ps1 scripts/demo-trafego.ps1
git commit -m "docs: roteiro do video da fase 3 + preflight e gerador de trafego"
git push -u origin docs/roteiro-e-preflight-video
gh pr create --base master --head docs/roteiro-e-preflight-video --title "docs: roteiro de gravacao, preflight e trafego de demonstracao" --body-file <ledger>/pr-body-roteiro.md
gh pr merge --merge
```

---

### Task 5: Gravação do vídeo (ação humana, com checklist)

**Files:** nenhum (produz o arquivo de vídeo).

- [ ] **Step 1: Preparar o ambiente de gravação**

- [ ] Windows na resolução final, terminal com fonte **≥ 16pt**, PowerShell em tela cheia (nada de fonte pequena: a banca precisa ler os comandos e as respostas).
- [ ] Abas abertas **antes** de gravar: Grafana `FCG - APIs`, Grafana `FCG - Logs (Loki)`, Prometheus `Targets`, README na seção *Arquitetura*, terminal principal, terminal do `watch` da função.
- [ ] `preflight-fase3.ps1` verde **na mesma sessão da gravação** (a stack precisa estar no estado inicial: função em 0 réplicas).
- [ ] `.env` fechado e fora de qualquer aba; nenhum token/segredo visível no histórico do terminal (`Clear-History` antes de gravar).

- [ ] **Step 2: Gravar em uma tomada só, seguindo o roteiro**

- [ ] Seguir a tabela de tempos da Task 4, na ordem; sem pausa para editar nada.
- [ ] Ler em voz alta o **que** está sendo provado em cada comando (a banca avalia a demonstração, não só o resultado).
- [ ] Deixar o `demo-trafego.ps1` rodando enquanto fala do dashboard (movimento em tempo real é o que o enunciado pede).
- [ ] Ao final, conferir o tempo: **≤ 10:00**.

- [ ] **Step 3: Conferir o vídeo contra a lista do enunciado (antes de publicar)**

- [ ] Requisições via Gateway, com roteamento **e** segurança (401 → login → 200)? 
- [ ] Função serverless acionada **e** log exibido na plataforma centralizada (Grafana/Loki)?
- [ ] Dashboard do Grafana com métricas em tempo real (Opção A)?
- [ ] Explicação de como o NoSQL foi integrado (Mongo + Redis, e o porquê)?
- [ ] Repositórios citados/mostrados (incluindo o link da função)?
- [ ] Nenhum segredo apareceu na tela?

---

### Task 6: Publicação, links e checklist final

**Files:**
- Modify: `.fase2-repos/fcg-orchestration/README.md` (substituir `*[link do vídeo]*` da Task 3 pelo link publicado)

- [ ] **Step 1: Publicar o vídeo** (YouTube como **não listado**, ou Google Drive com link "qualquer pessoa com o link") e copiar a URL.

- [ ] **Step 2: Colocar o link no README** — trocar exatamente o trecho:

```markdown
**Demonstração em vídeo:** *[link do vídeo]* — roteiro em [`docs/roteiro-video-fase3.md`](docs/roteiro-video-fase3.md).
```
por:
```markdown
**Demonstração em vídeo:** [Demonstração da Fase 3 (10 min)](<URL-PUBLICADA>) — roteiro em [`docs/roteiro-video-fase3.md`](docs/roteiro-video-fase3.md).
```

- [ ] **Step 3: PR do link** (mesmo fluxo: branch → PR → merge) e conferir que o link abre em janela anônima.

- [ ] **Step 4: Checklist final de entrega** — rodar uma vez e colar no relatório:

```powershell
foreach ($r in 'fcg-orchestration','fcg-users-api','fcg-catalog-api','fcg-payments-api','fcg-notifications-function','fcg-notifications-api') {
  $p = "C:\Users\Gustavo\fcg\.fase2-repos\$r"
  "${r}: " + (git -C $p log --oneline -1 origin/master)
}
git -C .fase2-repos\fcg-orchestration grep -n -I -E 'FCG@Password123|Fcg2024Test!|fcg-secret-key-2024' origin/master   # esperado: vazio
gh pr list --repo gustavoaa-dev/fcg-orchestration --state open    # esperado: vazio
```

---

## Anexo A — Entregáveis do enunciado (transcritos)

**Vídeo de até 10 minutos demonstrando a arquitetura em funcionamento:**
1. Requisições via Gateway, mostrando o roteamento e a segurança.
2. Demonstrar a Função Serverless sendo acionada e exibir seus logs na plataforma centralizada.
3. Apresentar a solução de observabilidade escolhida: Opção A — dashboard do Grafana com métricas em tempo real (Opção B seria Datadog/New Relic, com trace distribuído).
4. Explicar como o NoSQL foi integrado à arquitetura.

**Códigos-fonte nos repositórios:**
1. Atualizar os repositórios dos microsserviços com a instrumentação de observabilidade.
2. Link para o novo repositório da Função Serverless.
3. Atualizar o repositório de orquestração com os manifestos do API Gateway e da stack de monitoramento (Opção A).
4. Código atualizado com os novos drivers de NoSQL e Cache.
5. O `README.md` do repositório de orquestração deve ser o guia central, explicando a stack escolhida e como subir todo o ambiente.

## Self-review do plano

**Cobertura:** os 4 itens do vídeo → Tasks 2, 4 e 5 (roteiro com tempos por item); os 5 itens de código → instrumentação (Task 1) · link da função (já pronto, verificado na auditoria) · manifestos (já prontos) · drivers (já prontos) · README central (Tasks 3 e 6).

**Lacunas que este plano fecha:** `payments-api` sem instrumentação (Task 1) e ausência de logs centralizados (Task 2) — as duas únicas divergências encontradas na auditoria.

**Consistência:** nome da métrica confirmado em runtime antes de virar query de painel (Step 7 da Task 1); datasource `loki` (uid) igual no `grafana-configmap.yaml` e no dashboard; `app=notifications-function` é o mesmo label usado pelo `ScaledObject`, pelo `kubectl logs -l` do SP4 e pelo relabel do Promtail.
