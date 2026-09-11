# Spec — Evolução da Plataforma FCG (Fase 3): API Gateway, Serverless, Observabilidade, NoSQL e Cache

- **Data:** 2026-09-11
- **Escopo:** evolução do sistema de microsserviços FCG para a nova fase da disciplina (5 funcionalidades obrigatórias + requisitos técnicos), **sem reescrever o que já funciona**
- **Processo:** brainstorming (Superpowers) — contexto lido no código real dos 5 repositórios; decisões tomadas com o usuário em 2026-09-11 (seções 1–3 do design aprovadas)
- **Autoridade:** este documento é a fonte de verdade dos planos de implementação dos sub-projetos SP1–SP4

## 0. Contexto do workspace

- Repositório de trabalho (monolito da Fase 1): `C:\Users\Gustavo\fcg`, branch `fix/correcao-monolito` com o PR #1 aberto e **não mergeado** (correções do monolito; não bloqueia esta fase — recomendação: mergear antes de iniciar o SP1).
- Repositórios da Fase 2 clonados para análise em `.fase2-repos/` (pasta ignorada pelo git do monolito), cada um em seu próprio remoto:

| Repositório | HEAD analisado |
|---|---|
| `fcg-users-api` | `0d6dbb9` |
| `fcg-catalog-api` | `062009f` |
| `fcg-payments-api` | `f458633` |
| `fcg-notifications-api` | `8a3c725` |
| `fcg-orchestration` | `e985224` |

- Haverá um **repositório novo**: `fcg-notifications-function` (SP4).

## 1. Requisitos da nova fase (o que a disciplina pede)

**Problemas apontados:** exposição direta dos serviços (insegura e complexa para o cliente); falta de visibilidade (impossível saber qual serviço falhou, sem latência/saúde); desperdício de recursos (NotificationsAPI ocioso 24/7); gargalo de performance (dependência exclusiva de banco relacional).

**Desafio:** API Gateway; serverless para a NotificationsAPI; observabilidade (Opção A open-source ou Opção B APM); persistência poliglota (NoSQL); camada de cache.

**Funcionalidades obrigatórias:**

1. **API Gateway** — única porta de entrada; recebe todas as requisições, **valida token JWT** e **roteia para UsersAPI e CatalogAPI**; configuração (rotas, políticas) **versionada no repositório de orquestração**.
2. **Serverless** — refatorar a NotificationsAPI para **função serverless** acionada por mensagem da fila/tópico, substituindo o container contínuo; **código + IaC em repositório próprio**.
3. **Observabilidade** — escolher entre **Opção A** (Prometheus + Grafana: instrumentar UsersAPI/CatalogAPI, dashboards de latência, requisições total/por status e taxa de erro; implantação via manifestos Kubernetes) e **Opção B** (APM Datadog/New Relic com agentes em Users/Catalog/Payments + função, cobrindo métricas, logs e traces do fluxo de compra; chaves via Kubernetes Secrets); **a escolha deve ser documentada no README do repositório de orquestração**.
4. **Persistência poliglota (obrigatória)** — MongoDB ou DynamoDB, com driver oficial .NET, para dados flexíveis/alta volumetria (catálogo expandido, logs de eventos, perfis, avaliações).
5. **Cache distribuído (obrigatório)** — Redis (`StackExchange.Redis` ou `IDistributedCache`) para sessões, consultas onerosas ou configurações globais.

## 2. Estado atual verificado (evidência no código)

| Serviço | Superfície HTTP | Mensageria | Persistência | Observabilidade | Testes |
|---|---|---|---|---|---|
| `users-api` | `POST /api/auth/login`, `POST /api/usuarios`, `GET /api/usuarios` (Admin) | publica `UserCreatedEvent` (`Program.cs:91`) | SQL Server + EF, auto-migrate (`Program.cs:103-108`) | só console log | nenhum |
| `catalog-api` | `GET/POST/DELETE /api/jogos`, `POST /api/jogos/{id}/comprar`, `GET /api/biblioteca/{userId}` | publica `OrderPlacedEvent`, consome `PaymentProcessedEvent` | SQL Server + EF | só console log | nenhum |
| `payments-api` | **nenhuma rota** (event-driven puro) | consome `OrderPlaced`, publica `PaymentProcessed` | SQL Server + EF | só console log | nenhum |
| `notifications-api` | nenhuma rota; container 24/7 | consome `UserCreated` + `PaymentProcessed` | nenhuma | só console log | nenhum |
| `orchestration` | — | RabbitMQ (filas criadas em runtime pelo MassTransit) | SQL Server **sem volume/PVC** | **nada** | — |

| Requisito | Veredito | Lacuna principal |
|---|---|---|
| 1. Gateway | **Ausente** | nenhum gateway; `users`/`catalog` expostos por **NodePort 30001/30002**; JWT validado dentro de cada serviço com chave simétrica |
| 2. Serverless | **Ausente** | container MassTransit 24/7; sem IaC; sem testes |
| 3. Observabilidade | **Ausente** | zero métricas/tracing; **sem `/health`** (K8s só tem TCP probe) |
| 4. NoSQL | **Ausente** | apenas SQL Server; nenhum driver NoSQL |
| 5. Cache | **Ausente** | nenhum Redis/`IDistributedCache`; listagem do catálogo sem paginação sempre vai ao banco |

**Fundações ausentes que precisam entrar junto:** namespace/organização do cluster, `/health` + probes, estratégia de imagens sem registry, segredos em texto claro (senha `sa` e `Jwt__SecretKey` repetidos em manifestos/appsettings), `UseHttpsRedirection()` com container HTTP (risco de 307 atrás do gateway), `Database.Migrate()` no boot sem readiness, MassTransit 7.3.1 (API) vs 8.0.0 (Application/Infrastructure), contratos de evento duplicados por namespace (`FCG.Shared.Events`), zero testes nos 4 serviços.

## 3. Decisões aprovadas

| # | Decisão | Justificativa | Custo se errado |
|---|---|---|---|
| D1 | **Serverless = Azure Function (isolated worker .NET 8) com `RabbitMQTrigger`, hospedada no cluster Kubernetes local via KEDA (scale to zero)**, com IaC no repositório próprio | usuário não tem (e não consegue criar) conta de nuvem; KEDA + runtime do Functions é solução oficial e gratuita, acionada pela fila existente, portável para o Consumption plan depois ([Azure Functions on Kubernetes with KEDA](https://learn.microsoft.com/en-us/azure/azure-functions/functions-kubernetes-keda), [scaler RabbitMQ do KEDA](https://keda.sh/docs/2.20/scalers/rabbitmq-queue/)) | se o professor exigir FaaS gerenciado, o mesmo código sobe para Azure depois; muda apenas a infra de execução |
| D2 | **Cluster = Docker Desktop Kubernetes** | `LoadBalancer` resolve para `localhost` (Kong em `localhost:8000` sem Ingress); cluster vê as imagens construídas localmente (sem registry) | trocar de cluster exige recarregar imagens e revisar exposição |
| D3 | **Observabilidade = Opção A** (Prometheus + Grafana via manifestos; instrumentação com `prometheus-net` em Users/Catalog) | custo zero, sem conta/cartão, roda no cluster local; cobre exatamente as métricas pedidas | se optar por APM depois, é trabalho adicional (agentes + chaves), não retrabalho do que foi feito |
| D4 | **NoSQL = MongoDB com o cenário "avaliações de jogos"** no `catalog-api` | schema flexível real, dados gerados pelos usuários (sem seed), boa justificativa de volumetria; driver oficial `MongoDB.Driver` | outro cenário (log de eventos/perfis) exigiria refazer modelo e endpoints |
| D5 | **Cache = Redis com cache de leitura do catálogo** (`GET /api/jogos`, `GET /api/jogos/{id}`), TTL ~60s, invalidação explícita em POST/DELETE, **falha de cache não derruba a API** | é a consulta onerosa já identificada (`GameRepository.cs:22-25`); atende o requisito com o menor risco | cachear outra coisa (sessões/perfil) exigiria nova chave/estratégia |
| D6 | **Gateway = Kong declarativo (DB-less)** com plugin `jwt` em **HS256 usando o segredo atual** (via K8s Secret); serviços continuam validando e checando role; **NodePort/portas diretas fechados** | menor mudança possível (zero alteração de código nos 4 serviços) e cumpre "validar JWT + rotear" | migrar para RS256/JWKS depois é trabalho extra, mas localizado |
| D7 | **Abordagem = por sub-projeto** (SP1 Gateway → SP2 Observabilidade → SP3 NoSQL+Cache → SP4 Serverless), cada um com plano e ciclo próprios | dependências reais respeitadas; cada requisito vira entrega demonstrável isoladamente | ordem diferente atrasa a prova de algum requisito, mas não invalida o desenho |
| D8 | **Fora de escopo nesta fase:** RS256/JWKS, tracing distribuído/APM, Ingress controller, registry de imagens, outbox/idempotência, suíte de testes nova nos microsserviços, padronização de erros dos microsserviços, troca de broker | YAGNI / "caminho mais simples que contempla os requisitos" | fica como dívida documentada |
| D9 | **Avaliação de jogo: reavaliação do mesmo usuário/jogo sobrescreve** (upsert) em vez de 409 | idempotente, mais simples para o cliente e para o teste | se preferir histórico de avaliações, muda o modelo (array de avaliações por usuário) |
| D10 | **Credenciais GitHub via `gh` CLI (keyring)**; sandbox do agente **permanece** `workspace-write` + aprovação | escolha explícita do usuário; aprovações agrupadas por lote para reduzir interrupções | comandos de rede/dotnet continuam pedindo aprovação |

## 4. Arquitetura alvo

**Fluxo de uma requisição:**
`cliente → Kong (localhost:8000, valida JWT) → users-api | catalog-api (revalidam JWT, checam role) → SQL Server / MongoDB / Redis → evento no RabbitMQ → payments-api (publica PaymentProcessed) → função serverless (KEDA) executa a notificação`
Em paralelo: `Prometheus` raspa `/metrics` de users/catalog; `Grafana` lê do Prometheus.

**Rotas no gateway (config versionada em `fcg-orchestration/k8s/kong/`):**

| Rota | Destino | Observação |
|---|---|---|
| `POST /api/auth/login` | users-api | anônima |
| `POST /api/usuarios` | users-api | **anônima** (cadastro) |
| `GET/PUT/PATCH/DELETE /api/usuarios` | users-api | JWT validado no gateway |
| `/api/jogos*` | catalog-api | JWT validado no gateway; inclui `/{id}/avaliacoes` (SP3) |
| `/api/biblioteca*` | catalog-api | JWT validado no gateway |

> As rotas de `/api/usuarios` são separadas **por método** no Kong: `POST` (cadastro) fica sem o plugin `jwt`; os demais métodos exigem token — caso contrário o cadastro quebraria.

**Mudanças de topologia no cluster:** tudo permanece no **namespace `default`** (os 13 manifestos já estão nele; mudar de namespace exigiria FQDN em cada upstream); Kong (`Deployment` + `Service` `LoadBalancer`); `users`/`catalog` como `ClusterIP` (NodePort removido); `notifications` deixa de ser container e passa a ser função com `ScaledObject` do KEDA (escala 0↔N); Prometheus + Grafana; MongoDB; Redis; segredos; probes em todos os workloads de API; imagens locais (`imagePullPolicy: IfNotPresent`).

## 5. Sub-projetos

Cada sub-projeto terá seu **plano de implementação** próprio (skill `writing-plans`) e será executado com revisão por subagente. Definition of Done comum: entrega demonstrável conforme a tabela da seção 7 + documentação atualizada + nenhuma quebra dos fluxos existentes.

### SP1 — API Gateway Kong

- **Repositório:** `fcg-orchestration`.
- **Cria:** `k8s/kong/kong-deployment.yaml` (Deployment + Service `LoadBalancer` — proxy em 8000; Admin API em 8001 acessível apenas por `port-forward`), `k8s/kong/kong.yml.template` (config declarativa: services, rotas, plugin `jwt`, consumer com credencial HS256, segredo como `${JWT_SECRET}`) e `scripts/deploy-kong.ps1` (renderiza o template e recria o `Secret` `kong-declarative-config`).
- **Altera:** `k8s/users-api-*.yaml` e `k8s/catalog-api-*.yaml` (`NodePort` → `ClusterIP`), `docker-compose.yml` (remove publicação das portas 5001–5004), `README.md` (topologia de entrada única, como subir e testar).
- **Ajuste de integração — verificado como desnecessário:** o `UseHttpsRedirection()` (`users` `Program.cs:117`, `catalog` `Program.cs:121`) **não emite 307 hoje**, porque nenhum manifesto ou Dockerfile define `ASPNETCORE_HTTPS_PORTS`/URLs `https` e não há `UseForwardedHeaders`. Regra que passa a valer: **não** definir porta HTTPS para os serviços e manter o upstream do Kong em `http://`.
- **Segredo:** não entra no git. Fonte única é o `Secret users-api-secret` (chave `jwt-secret-key`) já existente no cluster; o script de deploy lê o valor, substitui no template e recria o `Secret kong-declarative-config`, montado no pod do Kong como arquivo de configuração declarativa (dispensa o vault de ambiente do Kong).
- **Verificação:** `curl http://localhost:8000/api/jogos` sem token → 401; com token → 200; porta direta do serviço não responde mais; `kubectl get svc` mostra `ClusterIP`.

### SP2 — Observabilidade (Opção A)

- **Repositórios:** `fcg-users-api`, `fcg-catalog-api`, `fcg-orchestration`.
- **Serviços:** adicionar `prometheus-net.AspNetCore` + `UseHttpMetrics()`/`MapMetrics()`; `AddHealthChecks()` + `MapHealthChecks("/health")`. Sem alteração dos contratos existentes das APIs.
- **Orquestração:** `k8s/prometheus/*` (ConfigMap de scrape por DNS interno, Deployment, Service, PVC), `k8s/grafana/*` (Deployment, Service, ConfigMaps de datasource e dashboards, senha do admin em Secret), probes `liveness/readiness` apontando para `/health` nos manifestos de users/catalog, e o `README.md` registrando formalmente a escolha da Opção A (exigência do enunciado).
- **Dashboards mínimos:** latência (p50/p95) por rota, requisições totais e por status HTTP, taxa de erros (5xx/total).
- **Verificação:** `/metrics` respondendo; alvo **UP** no Prometheus; dashboard exibindo tráfego gerado por curl; pods `Ready` com probes.

### SP3 — Persistência poliglota (MongoDB) + Cache (Redis)

- **Repositórios:** `fcg-catalog-api`, `fcg-orchestration`.
- **MongoDB (avaliações):** `MongoDB.Driver`; documento `Review` (`gameId`, `userId`, `nota` 1–5, `comentario`, `tags[]`, `dataCriacao`, `dataAtualizacao`); repositório e serviço de aplicação; regras: jogo precisa existir no SQL (404), nota fora de 1–5 → 400, uma avaliação por usuário/jogo com **upsert** (D9), `userId` vem do claim do token (nunca do corpo).
  - Endpoints: `POST /api/jogos/{gameId}/avaliacoes` (autenticado), `GET /api/jogos/{gameId}/avaliacoes` (lista + média). Já cobertos pela rota `/api/jogos*` do SP1.
- **Redis (cache):** `Microsoft.Extensions.Caching.StackExchangeRedis` (`IDistributedCache`); chaves `catalog:games:all` e `catalog:game:{id}`; TTL de **60 segundos**; invalidação no POST/DELETE de jogo; contadores `cache_hit`/`cache_miss` expostos no `/metrics` (aproveitando o SP2); indisponibilidade do Redis degrada para o SQL com log, nunca erro para o cliente.
- **Orquestração:** `k8s/mongodb/*` (Deployment + Service + PVC; credenciais em Secret), `k8s/redis/*` (Deployment + Service; senha opcional em Secret), variáveis de ambiente correspondentes no configmap/secret do catalog.
- **Verificação:** avaliação criada e lida do Mongo (dados sobrevivem a `kubectl delete pod` do mongo); `MongoDB.Driver` no `.csproj`; evidência de cache (segunda chamada mais rápida + contadores hit/miss); API continua respondendo com o Redis derrubado.

### SP4 — Serverless (função de notificações) + KEDA

- **Repositórios:** **novo** `fcg-notifications-function` + `fcg-orchestration`.
- **Repo novo:** Azure Function isolated worker (.NET 8) com `RabbitMQTrigger` nas filas que a notificação já consome (`UserCreatedEvent`, `PaymentProcessedEvent` — nomes exatos confirmados na leitura do código atual do `notifications-api`), reaproveitando a lógica de envio (hoje log estruturado). O comportamento é **sem estado**: a função registra o evento recebido; falha de processamento deixa a mensagem para o retry do trigger e, esgotadas as tentativas, ela vai para a DLQ (sem idempotência persistida nesta fase, pois o efeito é apenas log). Inclui `Dockerfile`, `host.json`, `local.settings.example.json` e **IaC no próprio repositório em Terraform** (alvo gerenciado) somado aos manifestos `Deployment` + `ScaledObject` para o cluster.
- **Orquestração:** instalação do **KEDA**, remoção de `k8s/notifications-api-*.yaml` e do bloco da notificação no `docker-compose.yml`, atualização do README (a função é implantada a partir do repo próprio).
- **Restrição de design:** **não alterar os publicadores** (users/catalog/payments) — a função se acopla às filas existentes.
- **Verificação:** publicar evento pelo fluxo de compra (via gateway) → execução registrada nos logs da função; **0 réplicas em repouso** (`kubectl get pods`); container antigo ausente.

## 6. Fundações transversais (entram nos SPs, não são sub-projetos)

- **Namespace:** tudo permanece no `default` (decisão verificada no planejamento do SP1 — os manifestos existentes já estão lá).
- **Segredos:** `jwt-secret`, credenciais do SQL, do RabbitMQ, do Mongo e senha do admin do Grafana — sempre via `Secret` criado no cluster; no git apenas `*.example.yaml` + comando documentado. Antes de cada commit: conferir com `git grep` que nenhum valor real entrou.
- **Imagens:** build local com `docker build`; `imagePullPolicy: IfNotPresent`; passo de rebuild documentado no README.
- **Saúde:** `/health` + probes em users/catalog. O `/health` **não** precisa consultar o banco: a migração roda de forma síncrona antes de a aplicação começar a escutar, então o `readinessProbe` falha naturalmente até a migração terminar.
- **Rede:** nenhuma porta de serviço publicada diretamente além do Kong.

## 7. Verificação por requisito (evidência exigida)

| Requisito | Evidência |
|---|---|
| 1. Gateway | curl sem token → 401 no Kong; curl com token → 200; acesso direto ao serviço indisponível; `kubectl get svc` com `ClusterIP` |
| 2. Serverless | evento de compra → log da função; `kubectl get pods` com 0 réplicas em repouso; manifests/compose do container removidos |
| 3. Observabilidade A | `/metrics` UP; alvo UP no Prometheus; dashboard Grafana com latência/throughput/erros sob carga; decisão no README do orchestration |
| 4. NoSQL | avaliação persistida e lida após restart do pod do Mongo; `MongoDB.Driver` referenciado |
| 5. Cache | hit/miss visíveis e latência menor na segunda chamada; API funcional com Redis fora do ar |
| Fundações | todos os pods `Ready`; nenhum segredo real no git; portas diretas fechadas |

## 8. Riscos e mitigação

1. **307 do HTTPS redirect atrás do Kong** → ajuste obrigatório no SP1 (configuração por ambiente).
2. **Nomes de fila criados em runtime pelo MassTransit** são o contrato com a função → SP4 confirma os nomes lendo o código e documenta o contrato no repo da função; mudanças futuras de nome exigem atualizar a função.
3. **Contratos de evento duplicados por namespace** nos 4 repos → a função valida payload e usa DLQ; unificação em pacote compartilhado fora de escopo.
4. **Zero testes nos microsserviços** → mudanças desta fase são mínimas e aditivas; verificação por execução real (curl/kubectl/logs) + revisão por subagente; nenhuma refatoração do existente.
5. **Sem registry de imagens** → build local + `imagePullPolicy: IfNotPresent`; trocar de cluster exige recarregar imagens.
6. **Persistência** → PVC obrigatório para Mongo (e para o Prometheus), senão os dados somem no restart do pod.
7. **KEDA no Docker Desktop** → instalação por manifesto único; é o ponto mais novo da fase, por isso o SP4 vem por último.
8. **Docker Desktop desligado/cluster parado** → a fase inteira é local; nada em nuvem.

## 9. Ordem de execução e fluxo de trabalho

1. **SP1 (Gateway)** → plano + implementação + PR em `fcg-orchestration`.
2. **SP2 (Observabilidade)** → PRs em `fcg-users-api`, `fcg-catalog-api` e `fcg-orchestration`.
3. **SP3 (Mongo + Redis + avaliações)** → PRs em `fcg-catalog-api` e `fcg-orchestration`.
4. **SP4 (Serverless + KEDA)** → PRs no repo novo `fcg-notifications-function` e em `fcg-orchestration`.
5. Cada sub-projeto: `writing-plans` → implementação com TDD onde houver projeto de teste → revisão por subagente (spec + qualidade) → PR via `gh`.
6. **Recomendação prévia:** mergear o PR #1 (monolito) para não misturar frentes.
