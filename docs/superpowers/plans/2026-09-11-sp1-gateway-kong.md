# SP1 — API Gateway Kong (DB-less) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Colocar o Kong como única porta de entrada HTTP do FCG, validando JWT (HS256) e roteando para `users-api` e `catalog-api`, fechando a exposição direta dos serviços — sem alterar código das aplicações.

**Architecture:** Kong em modo declarativo/DB-less (`KONG_DATABASE=off` + `KONG_DECLARATIVE_CONFIG`), com a configuração (services/routes/plugins/consumer) versionada como *template* no repositório de orquestração; o segredo JWT entra num `Secret` do Kubernetes gerado em tempo de deploy a partir do segredo que já existe no cluster (`users-api-secret`). Os serviços passam de `NodePort` para `ClusterIP` e o Kong vira `LoadBalancer` (Docker Desktop → `localhost:8000`).

**Tech Stack:** Kong Gateway 3.x (imagem `kong:3.10`), Kubernetes do Docker Desktop, `kubectl`, PowerShell para o script de render do segredo, YAML declarativo do Kong.

**Spec:** `docs/superpowers/specs/2026-09-11-fase3-gateway-serverless-observabilidade-design.md` (SP1)

## Global Constraints

- **Namespace: `default`** — os 13 manifestos existentes já estão nele; mover tudo para outro namespace exigiria FQDN em cada upstream. (Divergência declarada em relação à primeira versão da spec, que falava em `fcg`.)
- **Upstream sempre HTTP puro:** os containers escutam em `8080` sem certificado; os Services expõem `port: 80 → targetPort: 8080`. No Kong, `url: http://users-api:80` e `http://catalog-api:80` (nomes exatos dos Services, sem sufixo).
- **Nada de segredo em texto claro no git:** o template usa o marcador `${JWT_SECRET}`; o arquivo com o valor só existe em `Secret` no cluster e em arquivo temporário fora do repositório.
- **Zero alteração de código** em `fcg-users-api`, `fcg-catalog-api`, `fcg-payments-api`, `fcg-notifications-api`.
- **JWT HS256 com o segredo atual**, issuer `FCG.UsersAPI`, audience `FCG.Client`; a credencial do consumer no Kong usa `key: FCG.UsersAPI` (o plugin `jwt` casa a chave com o claim `iss`).
- **Rotas anônimas preservadas:** `POST /api/auth/login` e `POST /api/usuarios` **não** passam pelo plugin JWT (hoje não têm `[Authorize]`); todas as demais rotas de `users-api` e `catalog-api` exigem token.
- **Autorização por role continua no serviço** — o header `Authorization` precisa chegar intacto ao upstream (o plugin `jwt` do Kong não o remove por padrão).
- **Não definir** `ASPNETCORE_HTTPS_PORTS`/URLs `https` para os serviços: sem porta HTTPS determinável o `UseHttpsRedirection()` apenas loga aviso (não emite 307). Se alguém definir depois, o redirect passa a quebrar o roteamento.
- Imagens locais: `fcg-users-api:latest`, `fcg-catalog-api:latest`, `fcg-payments-api:latest`, `fcg-notifications-api:latest`, com `imagePullPolicy: IfNotPresent` (sem registry).
- Comandos de verificação usam `kubectl` e `curl`; nada em nuvem, nada pago.

---

## Mapa de arquivos

**Criar (em `fcg-orchestration`):**
- `k8s/kong/kong-deployment.yaml` — Deployment + Service `LoadBalancer` do Kong (proxy 8000; Admin 8001 só por port-forward).
- `k8s/kong/kong.yml.template` — config declarativa (services, routes, plugin `jwt`, consumer) com `${JWT_SECRET}`.
- `scripts/deploy-kong.ps1` — renderiza o template com o segredo lido do `users-api-secret` e (re)cria o `Secret` `kong-declarative-config`.

**Modificar (em `fcg-orchestration`):**
- `k8s/users-api-deployment.yaml` (Service `NodePort`+`nodePort: 30001` → `ClusterIP`, sem `nodePort`).
- `k8s/catalog-api-deployment.yaml` (idem, `nodePort: 30002`).
- `docker-compose.yml` (remover publicação das portas 5001–5004; manter 1433/5672/15672 como acesso dev de infraestrutura).
- `README.md` (topologia de entrada única, tabela de portas, como acessar/testar via Kong; remover menções a NodePort 30001/30002).

**Modificar (em `fcg`, repositório de documentação):**
- `docs/superpowers/specs/2026-09-11-fase3-gateway-serverless-observabilidade-design.md` — registrar as duas correções (namespace `default`; HTTPS redirect sem efeito hoje) e o mecanismo do segredo do Kong.

---

## Pré-requisitos (executar uma vez, antes da Task 1)

- [ ] **P1: Cluster ligado**

Run: `kubectl get nodes`
Expected: uma linha com `Ready` (Docker Desktop → Settings → Kubernetes → Enable Kubernetes). Se `kubectl` não existir, ele vem junto com o Docker Desktop.

- [ ] **P2: Imagens construídas localmente** (nomes exatos usados pelos manifestos)

Run (a partir de `fcg-orchestration`):
```bash
docker build -t fcg-users-api:latest ../fcg-users-api
docker build -t fcg-catalog-api:latest ../fcg-catalog-api
docker build -t fcg-payments-api:latest ../fcg-payments-api
docker build -t fcg-notifications-api:latest ../fcg-notifications-api
docker images | grep fcg-
```
Expected: as quatro imagens `fcg-*:latest`. (Nota: o Docker Desktop compartilha o daemon com o cluster, então não é necessário registry.)

---

### Task 1: Fechar a exposição direta dos serviços

**Files:**
- Modify: `k8s/users-api-deployment.yaml` (bloco `Service`, ~linhas 38-51)
- Modify: `k8s/catalog-api-deployment.yaml` (bloco `Service`, ~linhas 38-51)
- Modify: `docker-compose.yml` (linhas ~40, 62, 84, 103)

**Interfaces:**
- Produces: `Service/users-api` e `Service/catalog-api` do tipo `ClusterIP` na porta `80` → `targetPort: 8080` (é o endereço que o Kong usará como upstream).

- [ ] **Step 1: Trocar os dois Services para ClusterIP**

Em `k8s/users-api-deployment.yaml`, substituir o bloco do Service (a partir de `kind: Service`) por:

```yaml
kind: Service
apiVersion: v1
metadata:
  name: users-api
  labels:
    app: users-api
spec:
  type: ClusterIP
  selector:
    app: users-api
  ports:
    - name: http
      port: 80
      targetPort: 8080
```

Em `k8s/catalog-api-deployment.yaml`, o mesmo bloco com `name: catalog-api`, `app: catalog-api`.

- [ ] **Step 2: Remover a publicação das portas das APIs no compose**

Em `docker-compose.yml`, remover (ou comentar) a seção `ports:` de `users-api`, `catalog-api`, `payments-api` e `notifications-api` (linhas ~40, 62, 84, 103 — as que publicam `5001:8080`, `5002:8080`, `5003:8080`, `5004:8080`). **Manter** as portas de `rabbitmq` (5672/15672) e `sqlserver` (1433), que são acesso de infraestrutura.

- [ ] **Step 3: Aplicar no cluster e verificar**

Run:
```bash
kubectl apply -f k8s/
kubectl get svc users-api catalog-api -o custom-columns=NAME:.metadata.name,TYPE:.spec.type,PORT:.spec.ports[0].port
```
Expected: `users-api` e `catalog-api` com `TYPE` = `ClusterIP` e `PORT` = `80`.

- [ ] **Step 4: Confirmar que a porta direta morreu e que o serviço interno responde**

Run:
```bash
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:30001/api/jogos ; echo "exit=$?"
kubectl run curl-check --rm -i --restart=Never --image=curlimages/curl -- curl -s -o /dev/null -w "%{http_code}\n" http://users-api/api/usuarios
```
Expected: o primeiro comando **falha** (connection refused / exit != 0 — o NodePort não existe mais); o segundo retorna `401` (o Serviço responde internamente e a API exige autenticação). Se a imagem `curlimages/curl` não estiver acessível offline, use `kubectl port-forward svc/users-api 18080:80` em um terminal e `curl -o /dev/null -w "%{http_code}" http://localhost:18080/api/usuarios` (esperado `401`).

- [ ] **Step 5: Commit**

```bash
git add k8s/users-api-deployment.yaml k8s/catalog-api-deployment.yaml docker-compose.yml
git commit -m "feat: fecha exposicao direta das APIs (ClusterIP e sem portas publicadas)"
```

---

### Task 2: Kong DB-less no cluster (rotas sem JWT ainda)

**Files:**
- Create: `k8s/kong/kong-deployment.yaml`
- Create: `k8s/kong/kong.yml.template`
- Create: `scripts/deploy-kong.ps1`

**Interfaces:**
- Consumes: `Service/users-api` e `Service/catalog-api` na porta 80 (Task 1); `Secret/users-api-secret` com a chave `jwt-secret-key` (já existe no cluster).
- Produces: `Service/kong` (`LoadBalancer`, porta 8000) → proxy do Kong em `http://localhost:8000`; `Secret/kong-declarative-config` com a config declarativa renderizada.

- [ ] **Step 1: Criar o template da configuração declarativa (sem segredo, sem JWT ainda)**

Create `k8s/kong/kong.yml.template`:

```yaml
_format_version: "3.0"

services:
  - name: users-api
    url: http://users-api:80
    routes:
      - name: users-login
        paths:
          - /api/auth
        methods:
          - POST
        strip_path: false
      - name: users-signup
        paths:
          - /api/usuarios
        methods:
          - POST
        strip_path: false
      - name: users-protegida
        paths:
          - /api/usuarios
        methods:
          - GET
          - PUT
          - PATCH
          - DELETE
        strip_path: false

  - name: catalog-api
    url: http://catalog-api:80
    routes:
      - name: catalog-jogos
        paths:
          - /api/jogos
        strip_path: false
      - name: catalog-biblioteca
        paths:
          - /api/biblioteca
        strip_path: false

consumers:
  - username: fcg-client
    jwt_secrets:
      - key: FCG.UsersAPI
        algorithm: HS256
        secret: ${JWT_SECRET}
```

Observação: o `consumer` já entra nesta etapa (sem o plugin aplicado, ele é inerte) para que a Task 3 só precise adicionar os plugins.

- [ ] **Step 2: Criar o Deployment + Service do Kong**

Create `k8s/kong/kong-deployment.yaml`:

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: kong
  labels:
    app: kong
spec:
  replicas: 1
  selector:
    matchLabels:
      app: kong
  template:
    metadata:
      labels:
        app: kong
    spec:
      containers:
        - name: kong
          image: kong:3.10
          imagePullPolicy: IfNotPresent
          env:
            - name: KONG_DATABASE
              value: "off"
            - name: KONG_DECLARATIVE_CONFIG
              value: /kong/declarative/kong.yml
            - name: KONG_PROXY_LISTEN
              value: 0.0.0.0:8000
            - name: KONG_ADMIN_LISTEN
              value: 0.0.0.0:8001
            - name: KONG_PROXY_ACCESS_LOG
              value: /dev/stdout
            - name: KONG_ADMIN_ACCESS_LOG
              value: /dev/stdout
            - name: KONG_PROXY_ERROR_LOG
              value: /dev/stderr
            - name: KONG_ADMIN_ERROR_LOG
              value: /dev/stderr
            - name: KONG_LOG_LEVEL
              value: info
          ports:
            - name: proxy
              containerPort: 8000
            - name: admin
              containerPort: 8001
          readinessProbe:
            httpGet:
              path: /status
              port: admin
            initialDelaySeconds: 5
            periodSeconds: 5
          livenessProbe:
            httpGet:
              path: /status
              port: admin
            initialDelaySeconds: 15
            periodSeconds: 10
          volumeMounts:
            - name: declarative
              mountPath: /kong/declarative
              readOnly: true
      volumes:
        - name: declarative
          secret:
            secretName: kong-declarative-config
---
apiVersion: v1
kind: Service
metadata:
  name: kong
  labels:
    app: kong
spec:
  type: LoadBalancer
  selector:
    app: kong
  ports:
    - name: proxy
      port: 8000
      targetPort: proxy
```

Nota de segurança: a Admin API (8001) **não** é publicada pelo Service; para inspecionar use `kubectl port-forward deploy/kong 8001:8001`.

- [ ] **Step 3: Criar o script que renderiza o segredo a partir do cluster**

Create `scripts/deploy-kong.ps1`:

```powershell
# Renderiza k8s/kong/kong.yml a partir do template, substituindo ${JWT_SECRET} pelo segredo
# que ja existe no cluster (Secret users-api-secret, chave jwt-secret-key) e recria o
# Secret kong-declarative-config. Nenhum valor de segredo entra no repositorio.
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$templatePath = Join-Path $repoRoot 'k8s/kong/kong.yml.template'

if (-not (Test-Path $templatePath)) {
    throw "Template nao encontrado: $templatePath"
}

$secretBase64 = kubectl get secret users-api-secret -o jsonpath='{.data.jwt-secret-key}'
if (-not $secretBase64) {
    throw "Secret 'users-api-secret' (chave jwt-secret-key) nao encontrado no cluster. Aplique os manifestos das APIs antes de subir o Kong."
}
$jwtSecret = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($secretBase64))

$rendered = (Get-Content -Path $templatePath -Raw).Replace('${JWT_SECRET}', $jwtSecret)
$renderedPath = Join-Path $env:TEMP 'kong-rendered.yml'
Set-Content -Path $renderedPath -Value $rendered -Encoding UTF8 -NoNewline

kubectl create secret generic kong-declarative-config `
    --from-file=kong.yml=$renderedPath `
    --dry-run=client -o yaml | kubectl apply -f -

Remove-Item $renderedPath -Force

kubectl rollout restart deployment/kong
Write-Host "Secret kong-declarative-config atualizado e Kong reiniciado."
```

- [ ] **Step 4: Aplicar e verificar que o Kong subiu e roteia (sem JWT ainda)**

Run:
```bash
cd fcg-orchestration
kubectl apply -f k8s/kong/kong-deployment.yaml
pwsh -File scripts/deploy-kong.ps1
kubectl get pods -l app=kong
kubectl get svc kong
```
Expected: pod `kong-*` em `Running` (`READY 1/1`) e `Service/kong` com `EXTERNAL-IP: localhost` e porta `8000`.

- [ ] **Step 5: Verificar roteamento ponta a ponta (rota anônima)**

Run:
```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:8000/api/usuarios \
  -H "Content-Type: application/json" \
  -d '{"nome":"Teste Gateway","email":"gateway.teste@fcg.com","senha":"Senha@123"}'
```
Expected: `201` (o Kong encaminhou para `users-api`, que aceitou o cadastro) — ou `400`/`409` se o usuário já existir, o que também prova o roteamento (a resposta vem da aplicação, não do Kong). Uma resposta `502` significa upstream errado (conferir nome/porta do Service).

- [ ] **Step 6: Commit**

```bash
git add k8s/kong/ scripts/deploy-kong.ps1
git commit -m "feat: adiciona Kong DB-less como ponto de entrada unico (rotas e encaminhamento)"
```

---

### Task 3: Validação de JWT no gateway (rotas protegidas x anônimas)

**Files:**
- Modify: `k8s/kong/kong.yml.template` (adiciona o plugin `jwt` nas rotas protegidas)

**Interfaces:**
- Consumes: `consumer fcg-client` + `jwt_secrets` (Task 2); `Secret/users-api-secret` (fonte do segredo).
- Produces: gateway que responde `401` sem token e encaminha com token válido, preservando o header `Authorization` para os serviços.

- [ ] **Step 1: Aplicar o plugin JWT nas rotas protegidas do template**

Em `k8s/kong/kong.yml.template`, adicionar o bloco `plugins` dentro de `users-protegida` (rota `GET/PUT/PATCH/DELETE /api/usuarios`), `catalog-jogos` e `catalog-biblioteca`:

```yaml
        plugins:
          - name: jwt
            config:
              key_claim_name: iss
              claims_to_verify:
                - exp
              header_names:
                - authorization
              uri_param_names: []
              cookie_names: []
```

O bloco `plugins` fica **no mesmo nível** de `paths`/`methods`/`strip_path` (é plugin de rota). As rotas `users-login` e `users-signup` **não** recebem `plugins`.

- [ ] **Step 2: Reaplicar a configuração e verificar que o Kong recarregou**

Run:
```bash
pwsh -File scripts/deploy-kong.ps1
kubectl get pods -l app=kong
kubectl port-forward deploy/kong 8001:8001 &
curl -s http://localhost:8001/routes | head -c 400
```
Expected: pod `Running`; a lista de rotas do Kong mostra as 5 rotas (`users-login`, `users-signup`, `users-protegida`, `catalog-jogos`, `catalog-biblioteca`).

- [ ] **Step 2b: Confirmar qual imagem/tag do Kong foi usada (se o pod não subir)**

Se o pod ficar em `ImagePullBackOff`, a tag `kong:3.10` não existe no seu Docker: troque no `k8s/kong/kong-deployment.yaml` para `kong:3.9` (ou `kong:latest`), reaplique (`kubectl apply -f k8s/kong/kong-deployment.yaml`) e repita o Step 2.

- [ ] **Step 3: Verificar 401 sem token**

Run:
```bash
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:8000/api/jogos
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:8000/api/biblioteca/00000000-0000-0000-0000-000000000000
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:8000/api/usuarios
```
Expected: `401` nos três (sem token válido o Kong barra antes de chegar ao serviço).

- [ ] **Step 4: Verificar que as rotas anônimas continuam abertas**

Run:
```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:8000/api/usuarios \
  -H "Content-Type: application/json" \
  -d '{"nome":"Teste Gateway 2","email":"gateway.teste2@fcg.com","senha":"Senha@123"}'
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:8000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"gateway.teste@fcg.com","senha":"Senha@123"}'
```
Expected: `201` (cadastro) e `200` (login) — nenhuma das duas pede token no gateway.

- [ ] **Step 5: Verificar acesso autorizado com token**

Run:
```bash
TOKEN=$(curl -s -X POST http://localhost:8000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"gateway.teste@fcg.com","senha":"Senha@123"}' | sed -E 's/.*"token":"([^"]+)".*/\1/')
echo "token len: ${#TOKEN}"
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:8000/api/jogos -H "Authorization: Bearer $TOKEN"
```
Expected: `200` — o Kong validou o JWT (HS256 + claim `iss`) e encaminhou; o serviço recebeu o header intacto.

Se der `401` com token válido: conferir se o `iss` do token é exatamente `FCG.UsersAPI` e se a credencial do consumer usa a mesma `key`; se der `403`, a autorização por role do serviço está funcionando (esperado para rota Admin).

- [ ] **Step 6: Commit**

```bash
git add k8s/kong/kong.yml.template
git commit -m "feat: valida JWT (HS256) nas rotas protegidas do gateway"
```

---

### Task 4: Documentação e verificação final do SP1

**Files:**
- Modify: `fcg-orchestration/README.md` (tabela de portas ~L52-61; seção de acesso k8s ~L102-110; arquitetura ~L5-28; estrutura de arquivos ~L118-138)

**Interfaces:**
- Produces: instruções que qualquer pessoa (ou o professor) consegue seguir para subir e testar o gateway.

- [ ] **Step 1: Atualizar a documentação**

No `README.md` do `fcg-orchestration`:
1. Na tabela de portas (L52-61), substituir as linhas das APIs por uma nota: **“APIs não são publicadas diretamente: o acesso é feito pelo gateway Kong em `http://localhost:8000`”**; manter RabbitMQ Management (15672) e SQL Server (1433) como infraestrutura de desenvolvimento.
2. Na seção de deploy k8s (L69-116), substituir as instruções de `port-forward`/NodePort por:
   - `kubectl apply -f k8s/` (ordem: infra + APIs primeiro, depois `kubectl apply -f k8s/kong/kong-deployment.yaml`);
   - `pwsh -File scripts/deploy-kong.ps1` (renderiza o segredo e reinicia o Kong);
   - acesso: `http://localhost:8000`;
   - `port-forward` para depuração: `kubectl port-forward svc/users-api 8080:80` e Admin API do Kong `kubectl port-forward deploy/kong 8001:8001`.
3. Acrescentar uma subseção **“API Gateway (Kong)”** com: rotas expostas, onde a config vive (`k8s/kong/kong.yml.template`), como o segredo JWT é injetado (script + `Secret`, nada no git), e o aviso de que **não** se deve definir porta HTTPS nos serviços (o `UseHttpsRedirection()` passaria a responder 307 e quebraria o roteamento).
4. Atualizar a estrutura de arquivos (L118-138) incluindo `k8s/kong/` e `scripts/`.

- [ ] **Step 2: Verificação final consolidada (checklist do requisito 1)**

Run, na ordem:
```bash
kubectl get svc users-api catalog-api -o custom-columns=NAME:.metadata.name,TYPE:.spec.type
kubectl get svc kong
curl -s -o /dev/null -w "sem-token=%{http_code}\n" http://localhost:8000/api/jogos
curl -s -o /dev/null -w "com-token=%{http_code}\n" http://localhost:8000/api/jogos -H "Authorization: Bearer $TOKEN"
curl -s -o /dev/null -w "porta-direta=%{http_code}\n" http://localhost:30001/api/jogos
```
Expected: `ClusterIP` para os dois serviços; Kong com `EXTERNAL-IP localhost:8000`; `sem-token=401`; `com-token=200`; `porta-direta=000` (conexão recusada).

- [ ] **Step 3: Conferir que nenhum segredo entrou no git**

Run:
```bash
git grep -n "fcg-secret-key-2024" -- . || echo "OK: nenhum segredo literal novo"
git status --short
```
Expected: nenhuma ocorrência **nova** (as antigas em `docker-compose.yml`/`appsettings.json` fora do escopo do SP1 permanecem — dívida registrada) e árvore limpa após o commit.

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "docs: documenta o gateway Kong como ponto de entrada unico"
```

---

## Auto-review deste plano

- **Cobertura da spec (SP1):** gateway como única entrada (Tarefas 1 e 2) ✔; validação de JWT (Tarefa 3) ✔; roteamento para UsersAPI/CatalogAPI (Tarefas 2 e 3) ✔; configuração versionada no repositório de orquestração (Tarefas 2 e 3) ✔; ajuste do HTTPS redirect — verificado como **desnecessário** hoje (constraint registrada) ✔; documentação (Tarefa 4) ✔.
- **Placeholders:** nenhum “TBD/TODO”; todos os comandos, YAML e valores (nomes de Service, portas, issuer, imagens, rotas, marcador `${JWT_SECRET}`) estão explícitos.
- **Consistência de nomes:** `users-api`/`catalog-api` (Services), porta `80`, `targetPort: 8080`, `Secret kong-declarative-config`, `Secret users-api-secret/jwt-secret-key`, rotas `users-login`/`users-signup`/`users-protegida`/`catalog-jogos`/`catalog-biblioteca` — usados igualmente em todas as tarefas.
