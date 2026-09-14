# SP4 — Serverless (função de notificações) + KEDA: plano de execução

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Substituir o serviço `notifications-api` (container sempre ligado) por uma **Azure Function isolated worker .NET 8** implantada no cluster, consumindo os eventos de usuário e de pagamento via `RabbitMQTrigger`, com **escala a zero** pelo KEDA e a topologia do broker declarada em **Terraform**.

**Architecture:** A função vive num **repositório novo** (`fcg-notifications-function`) e é dona de **filas próprias** (`notifications-user-created`, `notifications-payment-processed`), ligadas aos exchanges fanout existentes (`UserCreatedEvent`, `PaymentProcessedEvent`) — assim cada evento chega a todos os interessados, em vez de competir com o `catalog-api` na fila `PaymentProcessed`. O KEDA escala o Deployment da função de 0 a 2 conforme o tamanho das filas; o Terraform (no repo novo) declara tanto a topologia do broker quanto os objetos da função no cluster. O container antigo sai do cluster, do compose e da documentação.

**Tech Stack:** .NET 8 isolated worker (`Microsoft.Azure.Functions.Worker` 1.24.0, `.Sdk` 1.18.1, `.Extensions.RabbitMQ` 2.1.0), imagem base `mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0`, KEDA (manifesto oficial de release), RabbitMQ 3-management (já no cluster, filas `UserCreated`/`PaymentProcessed`/`OrderPlaced`), Terraform com providers `cyrilgdn/rabbitmq` e `hashicorp/kubernetes`.

**Spec:** `docs/superpowers/specs/2026-09-11-fase3-gateway-serverless-observabilidade-design.md` (seções §122-128, decisão D1 e riscos §152-158)

## Global Constraints

- **Namespace:** a função e todos os objetos dela ficam no **`default`**; o KEDA fica no namespace `keda` que o próprio manifesto oficial cria (é plataforma, não aplicação).
- **Não alterar os publicadores** (`users-api`, `catalog-api`, `payments-api`): a função se acopla às filas/exchanges existentes. Nenhum arquivo desses serviços é tocado neste sub-projeto.
- **Não tocar na fila `PaymentProcessed`:** o `catalog-api` continua consumindo dela.
- **Nomes exatos:** filas `notifications-user-created` e `notifications-payment-processed`; DLX `fcg-notifications-dlx`; DLQ `notifications-dead-letter`; exchanges fanout de origem `UserCreatedEvent` e `PaymentProcessedEvent` (não são criados nem alterados por nós).
- **Segredo nunca no git:** o host AMQP (`amqp://guest:guest@rabbitmq:5672`) entra no cluster por `Secret` criado com `kubectl create secret ... --dry-run=client -o yaml | kubectl apply -f -`; no Terraform ele é **lido** por `data.kubernetes_secret`, nunca escrito. Antes de cada commit: `git grep -i -E 'amqp://|password'` no repo e conferir que só há referência.
- **Tag de imagem versionada:** `fcg-notifications-function:sp4-<sha7>` (nunca `:latest`), com `imagePullPolicy: IfNotPresent`. O build é local (`docker build`), o Dockerfile fica na raiz do repo novo.
- **Estado do Terraform é local e git-ignored:** `terraform.tfstate*`, `.terraform/`, `.terraform.lock.hcl` ficam fora do git (o lock pode ser versionado; o **state não**).
- **Verificação por execução real:** o SP4 não tem suíte de teste (D8 da spec). O aceite é `kubectl`, logs da função, `rabbitmqctl` e o `terraform apply`.
- Scripts `.ps1` de verificação: **ASCII puro**, sem BOM (PowerShell 5.1).
- Subagentes **editam e commitam**; o controlador roda `docker`, `kubectl`, `curl` e `terraform` (subagentes não têm permissão).
- Sem `--amend`, sem force-push, sem push em `master`: branch + PR. O repositório novo é criado pelo controlador com `gh repo create`.
- Comentários, mensagens de log e documentação em pt-BR.

## Mapa de arquivos

**`fcg-notifications-function`** (repositório novo, branch `fase3/sp4-serverless-keda`)

| Arquivo | Responsabilidade |
|---|---|
| `FCG.NotificationsFunction.csproj` (criar) | Projeto único, `net8.0`, `OutputType=Exe`, pacotes do worker e do trigger |
| `Program.cs` (criar) | Host do isolated worker + DI do `NotificationService` |
| `Functions/UserCreatedFunction.cs` (criar) | Trigger da fila `notifications-user-created` |
| `Functions/PaymentProcessedFunction.cs` (criar) | Trigger da fila `notifications-payment-processed` |
| `Services/NotificationService.cs` (criar) | Porte da lógica de envio (log estruturado), com as mesmas mensagens do serviço atual |
| `Events/UserCreatedEvent.cs`, `Events/PaymentProcessedEvent.cs` (criar) | Cópias locais dos contratos (namespace `FCG.Shared.Events`) — a spec já registra a duplicação de contratos como dívida conhecida |
| `host.json` (criar) | Configuração do host (`version` 2.0, logging, `extensions.rabbitMQ`) |
| `local.settings.example.json` (criar) | Exemplo de configuração local (sem valor real de segredo) |
| `Dockerfile` (criar) | Imagem da função para o cluster |
| `README.md` (criar) | O contrato: filas, exchanges, formato do payload, DLQ, como buildar/implantar e por que a função tem filas próprias |
| `.gitignore` (criar) | `bin/`, `obj/`, `local.settings.json`, estado do Terraform |
| `terraform/main.tf` (criar) | Providers, secret de conexão (por `data`), topologia do broker |
| `terraform/broker.tf` (criar) | Filas novas com DLX, DLQ, bindings nos exchanges existentes |
| `terraform/function.tf` (criar) | `Deployment`, `TriggerAuthentication` e `ScaledObject` da função |
| `terraform/variables.tf` (criar) | Variáveis (`image_tag`, `rabbitmq_management_url`, `namespace`) |
| `terraform/terraform.tfvars.example` (criar) | Exemplo de valores, sem segredo |

**`fcg-orchestration`** (branch `fase3/sp4-serverless-keda`, base = `master`)

| Arquivo | Responsabilidade |
|---|---|
| `k8s/notifications-api-deployment.yaml` (remover) | O serviço antigo deixa de existir |
| `docker-compose.yml` (modificar) | Remover o bloco `notifications-api` |
| `README.md` (modificar) | Seção do serverless: função, KEDA, escala a zero, contrato das filas, como implantar, e a nota de que a notificação saiu deste repositório |
| `k8s/keda/README.md` (criar) | Comando de instalação do KEDA (manifesto oficial) e como verificar |

**Ledger SDD (git-ignored, no repo `fcg`):** `.superpowers/sdd/2026-09-14-sp4-serverless-keda/` — `progress.md`, briefs, relatórios, revisões, diffs, logs de runtime e os scripts `verify-sp4-*.ps1`.

---

### Task 1: Repositório novo, a função e as filas (o risco do boot primeiro)

**Files:**
- Create: repositório `gustavoaa-dev/fcg-notifications-function` com `.gitignore`, `FCG.NotificationsFunction.csproj`, `Program.cs`, `Services/NotificationService.cs`, `Events/UserCreatedEvent.cs`, `Events/PaymentProcessedEvent.cs`, `Functions/UserCreatedFunction.cs`, `Functions/PaymentProcessedFunction.cs`, `host.json`, `local.settings.example.json`, `Dockerfile`
- Create: `terraform/variables.tf`, `terraform/broker.tf`, `terraform/terraform.tfvars.example`
- Modify: `README.md` (criado nesta task, com o contrato das filas)

**Interfaces:**
- Consumes: cluster com RabbitMQ (`deploy/rabbitmq`, exchanges fanout `UserCreatedEvent`/`PaymentProcessedEvent` já existentes) e `Secret rabbitmq-connection`.
- Produces: filas `notifications-user-created` e `notifications-payment-processed` (com DLX), DLQ `notifications-dead-letter`, imagem `fcg-notifications-function:sp4-<sha7>`, e a resposta empírica para **"o host do Functions sobe sem `AzureWebJobsStorage`?"** — insumo obrigatório da Task 2.

- [ ] **Step 1: Criar o repositório no GitHub e clonar (controlador)**

```powershell
& 'C:\Program Files\GitHub CLI\gh.exe' repo create gustavoaa-dev/fcg-notifications-function --private --description "Funcao serverless de notificacoes do FCG (SP4 - Azure Functions + KEDA)"
Set-Location 'C:\Users\Gustavo\fcg\.fase2-repos'
git clone https://github.com/gustavoaa-dev/fcg-notifications-function.git
Set-Location fcg-notifications-function
git checkout -b fase3/sp4-serverless-keda
git config user.name 'Gustavo A. Araujo'
git config user.email '126591395+gustavoaa-dev@users.noreply.github.com'
```

Se o repositório já existir, pule o `repo create` e apenas clone.

> O repositório nasce **privado** porque é trabalho de faculdade; mude para público depois se precisar.

- [ ] **Step 2: `.gitignore`**

```gitignore
bin/
obj/
local.settings.json
.terraform/
.terraform.lock.hcl
*.tfstate
*.tfstate.*
crash.log
terraform.tfvars
```

- [ ] **Step 3: `FCG.NotificationsFunction.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AzureFunctionsVersion>v4</AzureFunctionsVersion>
    <OutputType>Exe</OutputType>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>FCG.NotificationsFunction</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Azure.Functions.Worker" Version="1.24.0" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="1.18.1" />
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.RabbitMQ" Version="2.1.0" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: `Events/UserCreatedEvent.cs` e `Events/PaymentProcessedEvent.cs`**

```csharp
namespace FCG.Shared.Events;

public class UserCreatedEvent
{
    public Guid UserId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime DataCadastro { get; set; }
}
```

```csharp
namespace FCG.Shared.Events;

public class PaymentProcessedEvent
{
    public Guid OrderId { get; set; }
    public Guid UserId { get; set; }
    public Guid GameId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
}
```

> O namespace `FCG.Shared.Events` é mantido para os contratos ficarem idênticos aos dos serviços; a duplicação entre repositórios é dívida já registrada na spec (§153).

- [ ] **Step 5: `Services/NotificationService.cs`**

```csharp
using FCG.Shared.Events;
using Microsoft.Extensions.Logging;

namespace FCG.NotificationsFunction.Services;

/// <summary>
/// Mesmo efeito do antigo notifications-api: "envio" de e-mail registrado em log estruturado.
/// Mensagens idênticas às do serviço removido, para a evidência ser comparável.
/// </summary>
public class NotificationService
{
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
    }

    public void EnviarBoasVindas(UserCreatedEvent evento)
    {
        _logger.LogInformation(
            "[EMAIL ENVIADO] Boas-vindas para {Nome} - {Email}\n" +
            "    Assunto: Bem-vindo à FCG!\n" +
            "    Corpo: Olá {Nome}, seu cadastro foi realizado com sucesso!",
            evento.Nome, evento.Email, evento.Nome);
    }

    public void EnviarConfirmacaoCompra(PaymentProcessedEvent evento)
    {
        if (evento.Status == "Approved")
        {
            _logger.LogInformation(
                "[EMAIL ENVIADO] Confirmação de compra para UserId: {UserId}\n" +
                "    Assunto: Sua compra foi aprovada!\n" +
                "    Corpo: Parabéns! O jogo foi adicionado à sua biblioteca.",
                evento.UserId);
        }
        else
        {
            _logger.LogInformation(
                "[EMAIL ENVIADO] Falha na compra para UserId: {UserId}\n" +
                "    Assunto: Problema com seu pagamento\n" +
                "    Corpo: Infelizmente seu pagamento foi recusado. Tente novamente.",
                evento.UserId);
        }
    }
}
```

- [ ] **Step 6: `Functions/UserCreatedFunction.cs`**

```csharp
using System.Text.Json;
using FCG.NotificationsFunction.Services;
using FCG.Shared.Events;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace FCG.NotificationsFunction.Functions;

public class UserCreatedFunction
{
    // O trigger entrega o corpo como string; desserializamos com case-insensitive para
    // aceitar tanto camelCase quanto PascalCase (o serializador do publicador nao e nosso contrato).
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly NotificationService _notificationService;
    private readonly ILogger<UserCreatedFunction> _logger;

    public UserCreatedFunction(NotificationService notificationService, ILogger<UserCreatedFunction> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    [Function("UserCreated")]
    public void Run([RabbitMQTrigger("notifications-user-created", ConnectionStringSetting = "RabbitMQConnection")] string corpo)
    {
        // Propagar a excecao e proposital: o trigger devolve a mensagem para nova tentativa e,
        // esgotadas as tentativas, o broker a encaminha para a DLX configurada nas filas.
        var evento = JsonSerializer.Deserialize<UserCreatedEvent>(corpo, Json)
            ?? throw new InvalidOperationException("Payload vazio para UserCreatedEvent.");

        _logger.LogInformation("Evento UserCreated recebido - UserId: {UserId}, Email: {Email}", evento.UserId, evento.Email);
        _notificationService.EnviarBoasVindas(evento);
    }
}
```

- [ ] **Step 7: `Functions/PaymentProcessedFunction.cs`**

```csharp
using System.Text.Json;
using FCG.NotificationsFunction.Services;
using FCG.Shared.Events;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace FCG.NotificationsFunction.Functions;

public class PaymentProcessedFunction
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly NotificationService _notificationService;
    private readonly ILogger<PaymentProcessedFunction> _logger;

    public PaymentProcessedFunction(NotificationService notificationService, ILogger<PaymentProcessedFunction> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    [Function("PaymentProcessed")]
    public void Run([RabbitMQTrigger("notifications-payment-processed", ConnectionStringSetting = "RabbitMQConnection")] string corpo)
    {
        var evento = JsonSerializer.Deserialize<PaymentProcessedEvent>(corpo, Json)
            ?? throw new InvalidOperationException("Payload vazio para PaymentProcessedEvent.");

        _logger.LogInformation("Evento PaymentProcessed recebido - OrderId: {OrderId}, Status: {Status}", evento.OrderId, evento.Status);
        _notificationService.EnviarConfirmacaoCompra(evento);
    }
}
```

- [ ] **Step 8: `Program.cs`**

```csharp
using FCG.NotificationsFunction.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddSingleton<NotificationService>();
    })
    .Build();

host.Run();
```

- [ ] **Step 9: `host.json` e `local.settings.example.json`**

```json
{
  "version": "2.0",
  "logging": {
    "logLevel": {
      "default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "extensions": {
    "rabbitMQ": {
      "prefetchCount": 1
    }
  }
}
```

```json
{
  "IsEncrypted": false,
  "Values": {
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "RabbitMQConnection": "amqp://guest:guest@localhost:5672",
    "AzureWebJobsStorage": ""
  }
}
```

- [ ] **Step 10: `Dockerfile`**

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY FCG.NotificationsFunction.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish FCG.NotificationsFunction.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true \
    FUNCTIONS_WORKER_RUNTIME=dotnet-isolated
COPY --from=build /app/publish /home/site/wwwroot
```

- [ ] **Step 11: `terraform/variables.tf`**

```hcl
variable "namespace" {
  description = "Namespace do cluster onde a funcao vive."
  type        = string
  default     = "default"
}

variable "image_tag" {
  description = "Tag versionada da imagem da funcao (sp4-<sha7>)."
  type        = string
}

variable "rabbitmq_management_url" {
  description = "URL da API de gerenciamento do RabbitMQ (via port-forward)."
  type        = string
  default     = "http://localhost:15672"
}

variable "rabbitmq_username" {
  description = "Usuario do RabbitMQ para a API de gerenciamento."
  type        = string
  default     = "guest"
}

variable "rabbitmq_password" {
  description = "Senha do RabbitMQ para a API de gerenciamento (nao versionar valor real)."
  type        = string
  sensitive   = true
  default     = "guest"
}
```

- [ ] **Step 12: `terraform/broker.tf`**

```hcl
terraform {
  required_version = ">= 1.6.0"
  required_providers {
    rabbitmq = {
      source  = "cyrilgdn/rabbitmq"
      version = "1.9.0"
    }
    kubernetes = {
      source  = "hashicorp/kubernetes"
      version = "2.35.1"
    }
  }
}

provider "rabbitmq" {
  endpoint = var.rabbitmq_management_url
  username = var.rabbitmq_username
  password = var.rabbitmq_password
}

locals {
  vhost        = "/"
  dlx          = "fcg-notifications-dlx"
  dlq          = "notifications-dead-letter"
  filas = {
    user_created      = { nome = "notifications-user-created", exchange = "UserCreatedEvent" }
    payment_processed = { nome = "notifications-payment-processed", exchange = "PaymentProcessedEvent" }
  }
}

# DLX/DLQ: e o que torna a DLQ verdadeira em vez de teorica -- mensagem que esgota as
# tentativas do trigger e encaminhada pelo broker para ca.
resource "rabbitmq_exchange" "dlx" {
  name  = local.dlx
  vhost = local.vhost
  settings {
    type        = "fanout"
    durable     = true
    auto_delete = false
  }
}

resource "rabbitmq_queue" "dlq" {
  name  = local.dlq
  vhost = local.vhost
  settings {
    durable     = true
    auto_delete = false
  }
}

resource "rabbitmq_binding" "dlq_ao_dlx" {
  source           = local.dlx
  vhost            = local.vhost
  destination      = rabbitmq_queue.dlq.name
  destination_type = "queue"
  routing_key      = ""
}

resource "rabbitmq_queue" "fila" {
  for_each = local.filas
  name     = each.value.nome
  vhost    = local.vhost
  settings {
    durable     = true
    auto_delete = false
    arguments = {
      "x-dead-letter-exchange" = local.dlx
    }
  }
}

# O exchange de origem (UserCreatedEvent / PaymentProcessedEvent) e criado pelo MassTransit
# dos publicadores e NAO e gerenciado aqui -- o binding apenas se liga a ele por nome.
resource "rabbitmq_binding" "fila_ao_exchange" {
  for_each         = local.filas
  source           = each.value.exchange
  vhost            = local.vhost
  destination      = rabbitmq_queue.fila[each.key].name
  destination_type = "queue"
  routing_key      = ""
}
```

- [ ] **Step 13: `terraform/terraform.tfvars.example`**

```hcl
# Copie para terraform.tfvars e ajuste. O arquivo terraform.tfvars e git-ignored.
namespace                = "default"
image_tag                = "sp4-0000000"
rabbitmq_management_url  = "http://localhost:15672"
rabbitmq_username        = "guest"
rabbitmq_password        = "guest"
```

- [ ] **Step 14: `README.md` do repo novo**

Conteúdo obrigatório, em pt-BR:

- **O que é:** função serverless de notificações do FCG (isolated worker .NET 8), que substitui o antigo `notifications-api`.
- **Contrato das filas (o ponto mais importante):** a função consome `notifications-user-created` e `notifications-payment-processed`, filas **próprias**, ligadas por binding aos exchanges **fanout** `UserCreatedEvent` e `PaymentProcessedEvent`. O acoplamento é com o **nome do exchange** e com o **JSON do evento** — não com o nome da fila. Payloads:

  | Evento | Campos |
  |---|---|
  | `UserCreatedEvent` | `UserId` (Guid), `Nome`, `Email`, `DataCadastro` |
  | `PaymentProcessedEvent` | `OrderId` (Guid), `UserId` (Guid), `GameId` (Guid), `Status`, `ProcessedAt` |

  A desserialização é **case-insensitive**, então camelCase ou PascalCase funcionam.
- **Por que filas próprias (e não as do serviço antigo):** a fila `PaymentProcessed` era compartilhada com o `catalog-api`, ou seja, cada evento era entregue a **um** dos dois (consumidores concorrentes). Com fila própria, cada interessado recebe sua cópia e a notificação sempre dispara. Consequência operacional: a fila antiga `UserCreated` fica órfã e deve ser removida (`rabbitmqctl delete_queue UserCreated`), enquanto `PaymentProcessed` continua com o `catalog-api`.
- **Erro e DLQ:** a função não captura exceção de processamento; o trigger devolve a mensagem para retry e o broker a encaminha para `fcg-notifications-dlx` → `notifications-dead-letter` depois das tentativas. Sem idempotência persistida (o efeito é apenas log).
- **Build e implantação:** `docker build -t fcg-notifications-function:sp4-<sha7> .` na raiz; a implantação no cluster é feita pelo **Terraform** (`terraform init && terraform apply`), que declara o `Deployment`, o `TriggerAuthentication` e o `ScaledObject`; o KEDA é instalado à parte pelo manifesto oficial (veja o README do `fcg-orchestration`).
- **Segredo:** o `Secret rabbitmq-connection` (chave `host` = `amqp://guest:guest@rabbitmq:5672`) é criado por comando no cluster e não vai para o git.

- [ ] **Step 15: Commit inicial e push (implementador commita; controlador publica)**

```bash
git add .
git commit -m "feat: funcao de notificacoes em isolated worker com trigger de rabbitmq"
```

- [ ] **Step 16: Criar o Secret, as filas e subir a função (controlador)**

```powershell
kubectl create secret generic rabbitmq-connection --from-literal=host='amqp://guest:guest@rabbitmq:5672' --dry-run=client -o yaml | kubectl apply -f -
kubectl port-forward svc/rabbitmq 15672:15672   # em outra janela; a API de gerenciamento e o que o provider usa
cd C:\Users\Gustavo\fcg\.fase2-repos\fcg-notifications-function\terraform
terraform init
terraform apply -auto-approve -var "image_tag=sp4-<sha7>"
kubectl exec deploy/rabbitmq -- rabbitmqctl list_queues name messages consumers
```

Esperado nas filas: `notifications-user-created`, `notifications-payment-processed` e `notifications-dead-letter` existindo (0 mensagens, 0 consumidores ainda).

- [ ] **Step 17: O teste do risco — a função sobe sem `AzureWebJobsStorage`? (controlador)**

```powershell
docker build -t fcg-notifications-function:sp4-<sha7> .
# O YAML do probe vai por ARQUIVO (heredoc de bash nao existe no PowerShell).
@'
apiVersion: apps/v1
kind: Deployment
metadata:
  name: notifications-function-probe
  labels:
    app: notifications-function-probe
spec:
  replicas: 1
  selector:
    matchLabels:
      app: notifications-function-probe
  template:
    metadata:
      labels:
        app: notifications-function-probe
    spec:
      containers:
        - name: function
          image: fcg-notifications-function:sp4-<sha7>
          imagePullPolicy: IfNotPresent
          env:
            - name: RabbitMQConnection
              valueFrom:
                secretKeyRef:
                  name: rabbitmq-connection
                  key: host
          ports:
            - containerPort: 80
'@ | Set-Content -Path .superpowers\sdd\2026-09-14-sp4-serverless-keda\probe-function.yaml -Encoding ascii
kubectl apply -f .superpowers\sdd\2026-09-14-sp4-serverless-keda\probe-function.yaml
Start-Sleep -Seconds 25
kubectl logs deploy/notifications-function-probe --tail=60
```

**Decisão desta etapa (o resultado vira insumo obrigatório da Task 2):**

- Se o host **subir** (`Host started`, worker `dotnet-isolated` conectado e sem erro fatal), a Task 2 segue **sem** Azurite e registra o valor de `AzureWebJobsStorage` que funcionou.
- Se o host **falhar** por storage (mensagem citando `AzureWebJobsStorage`/`UseDevelopmentStorage`), a Task 2 acrescenta ao Terraform um `Deployment`+`Service` do **Azurite** (`mcr.microsoft.com/azure-storage/azurite`) e define `AzureWebJobsStorage=UseDevelopmentStorage=true` na função, com o comentário explicando que o host do Functions exige um storage mesmo para trigger de fila. O probe é então removido do cluster.

- [ ] **Step 18: Registro do resultado**

Anotar no relatório da task: (a) saída do `terraform apply`; (b) as três filas criadas; (c) o veredito do Step 17 **com as linhas de log que o sustentam**; (d) a imagem construída.

---

### Task 2: KEDA, ScaledObject e escala a zero

**Files:**
- Create: `terraform/function.tf`
- Modify: `terraform/variables.tf` (se o Azurite for necessário), `README.md` (seção de escala a zero)

**Interfaces:**
- Consumes: imagem `fcg-notifications-function:sp4-<sha7>`, `Secret rabbitmq-connection` (chave `host`), filas da Task 1, e o veredito do Step 17 sobre `AzureWebJobsStorage`.
- Produces: `Deployment notifications-function` (namespace `default`), `TriggerAuthentication notifications-rabbitmq`, `ScaledObject notifications-function` com `minReplicaCount: 0` e `maxReplicaCount: 2`.

- [ ] **Step 1: Instalar o KEDA pelo manifesto oficial (controlador)**

```powershell
kubectl apply --server-side -f https://github.com/kedacore/keda/releases/download/<VERSAO>/keda-<VERSAO>.yaml
kubectl get pods -n keda
kubectl get crd scaledobjects.keda.sh
```

A `<VERSAO>` é a última release estável do KEDA (ex.: `v2.18.0`) — confira em `https://github.com/kedacore/keda/releases` e registre a versão usada no README do orchestration. O manifesto cria o namespace `keda`; nada mais precisa ser alterado no cluster.

- [ ] **Step 2: `terraform/function.tf`**

```hcl
data "kubernetes_secret" "rabbitmq" {
  metadata {
    name      = "rabbitmq-connection"
    namespace = var.namespace
  }
}

resource "kubernetes_deployment" "function" {
  metadata {
    name      = "notifications-function"
    namespace = var.namespace
    labels = {
      app = "notifications-function"
    }
  }

  spec {
    replicas = 1 # o ScaledObject assume o controle a partir daqui (0 a 2)

    selector {
      match_labels = {
        app = "notifications-function"
      }
    }

    template {
      metadata {
        labels = {
          app = "notifications-function"
        }
      }

      spec {
        container {
          name              = "function"
          image             = "fcg-notifications-function:${var.image_tag}"
          image_pull_policy = "IfNotPresent"

          port {
            container_port = 80
          }

          env {
            name  = "FUNCTIONS_WORKER_RUNTIME"
            value = "dotnet-isolated"
          }

          env {
            name = "RabbitMQConnection"
            value_from {
              secret_key_ref {
                name = "rabbitmq-connection"
                key  = "host"
              }
            }
          }

          # O host do Functions exige um storage mesmo para trigger de fila; sem Azurite o valor
          # vazio faz o host subir com aviso (comportamento confirmado na Task 1, Step 17).
          env {
            name  = "AzureWebJobsStorage"
            value = ""
          }

          readiness_probe {
            http_get {
              path = "/"
              port = 80
            }
            initial_delay_seconds = 10
            period_seconds        = 5
          }

          resources {
            limits = {
              memory = "512Mi"
              cpu    = "500m"
            }
          }
        }
      }
    }
  }
}

resource "kubernetes_manifest" "trigger_authentication" {
  manifest = {
    apiVersion = "keda.sh/v1alpha1"
    kind       = "TriggerAuthentication"
    metadata = {
      name      = "notifications-rabbitmq"
      namespace = var.namespace
    }
    spec = {
      secretTargetRef = [
        {
          parameter = "host"
          name      = "rabbitmq-connection"
          key       = "host"
        }
      ]
    }
  }
}

resource "kubernetes_manifest" "scaled_object" {
  manifest = {
    apiVersion = "keda.sh/v1alpha1"
    kind       = "ScaledObject"
    metadata = {
      name      = "notifications-function"
      namespace = var.namespace
    }
    spec = {
      scaleTargetRef = {
        name = kubernetes_deployment.function.metadata[0].name
      }
      minReplicaCount = 0
      maxReplicaCount = 2
      pollingInterval = 15
      cooldownPeriod  = 30
      triggers = [
        {
          type = "rabbitmq"
          authenticationRef = {
            name = kubernetes_manifest.trigger_authentication.manifest.metadata.name
          }
          metadata = {
            queueName = "notifications-user-created"
            mode      = "QueueLength"
            value     = "1"
          }
        },
        {
          type = "rabbitmq"
          authenticationRef = {
            name = kubernetes_manifest.trigger_authentication.manifest.metadata.name
          }
          metadata = {
            queueName = "notifications-payment-processed"
            mode      = "QueueLength"
            value     = "1"
          }
        }
      ]
    }
  }
}
```

> A `TriggerAuthentication` fornece o parâmetro `host` ao scaler a partir do `Secret`; por isso o `metadata` dos triggers **não** repete o host (e nenhum segredo aparece no repositório).

- [ ] **Step 3: Aplicar e verificar o ScaledObject (controlador)**

```powershell
cd C:\Users\Gustavo\fcg\.fase2-repos\fcg-notifications-function\terraform
terraform apply -auto-approve -var "image_tag=sp4-<sha7>"
kubectl get scaledobject notifications-function -o wide
kubectl get pods -l app=notifications-function
```

Esperado: `ScaledObject` com `READY=True`; **nenhum pod** da função em repouso (`minReplicaCount: 0`).

- [ ] **Step 4: Commit**

```bash
git add terraform/function.tf terraform/variables.tf README.md
git commit -m "feat: escala a zero da funcao com keda"
```

---

### Task 3: Remover o serviço antigo e documentar no orchestration

**Files:**
- Delete: `k8s/notifications-api-deployment.yaml`
- Modify: `docker-compose.yml` (remover o bloco `notifications-api`), `README.md`
- Create: `k8s/keda/README.md`

**Interfaces:**
- Consumes: a função já rodando com KEDA (Task 2) — a remoção só é segura depois disso.
- Produces: cluster sem o serviço antigo; documentação do serverless no README do orchestration.

- [ ] **Step 1: Confirmar que a função cobre o fluxo antes de remover**

```powershell
kubectl get scaledobject notifications-function
kubectl get pods -l app=notifications-function
kubectl exec deploy/rabbitmq -- rabbitmqctl list_queues name messages consumers
kubectl exec deploy/rabbitmq -- rabbitmqctl list_consumers queue
```

Esperado: as duas filas novas aparecem com consumidor quando há pod, e o serviço antigo ainda está no ar (a remoção é o próximo passo).

- [ ] **Step 2: Remover o manifesto antigo e o bloco do compose**

```powershell
git rm k8s/notifications-api-deployment.yaml
kubectl delete deployment notifications-api   # se ainda existir
```

No `docker-compose.yml`, remover o serviço `notifications-api` **inteiro** (bloco `notifications-api:` com `build`, `container_name`, `environment` e `depends_on`).

- [ ] **Step 3: `k8s/keda/README.md`**

```markdown
# KEDA (escala a zero da função de notificações)

O operador é instalado pelo manifesto oficial de release (não há Helm neste ambiente):

```powershell
kubectl apply --server-side -f https://github.com/kedacore/keda/releases/download/v2.18.0/keda-2.18.0.yaml
kubectl get pods -n keda
kubectl get crd scaledobjects.keda.sh
```

A função de notificações é implantada a partir do repositório próprio
(`fcg-notifications-function`), cujo Terraform declara o `Deployment`, o `TriggerAuthentication` e o
`ScaledObject`. Este diretório guarda apenas o procedimento do operador.

Verificação rápida:

```powershell
kubectl get scaledobject notifications-function -o wide   # READY=True
kubectl get pods -l app=notifications-function            # vazio em repouso (minReplicaCount: 0)
```
```

- [ ] **Step 4: README do orchestration — seção `## Serverless (função de notificações)`**

Conteúdo obrigatório:

- **O que mudou:** a notificação deixou de ser um serviço sempre ligado (`ClusterIP` consumindo fila) e passou a ser uma **função com escala a zero**, implantada do repositório `fcg-notifications-function`.
- **Por que:** o requisito da fase é *serverless* — o serviço fica em **0 réplicas** em repouso e sobe quando chega evento, o que um Deployment comum não faz.
- **Como funciona:** KEDA observa o tamanho das filas `notifications-user-created` e `notifications-payment-processed` e escala o Deployment de 0 a 2; o RabbitMQ entrega o evento ao `RabbitMQTrigger`; o "envio" é log estruturado (`[EMAIL ENVIADO] ...`).
- **Contrato das filas:** filas próprias ligadas aos exchanges **fanout** `UserCreatedEvent` e `PaymentProcessedEvent`; o acoplamento é com o exchange e o JSON do evento. Detalhe completo no README do repositório da função.
- **Erro/DLQ:** retry do trigger e, esgotadas as tentativas, `fcg-notifications-dlx` → `notifications-dead-letter`.
- **Tabela de serviços e portas:** a linha do `notifications-api` sai; entra a observação de que a notificação não tem porta publicada (é função, sem superfície HTTP).
- **Limpeza de migração:** a fila `UserCreated` (a antiga, do container) fica órfã e deve ser removida com `kubectl exec deploy/rabbitmq -- rabbitmqctl delete_queue UserCreated`; a `PaymentProcessed` permanece, pois o `catalog-api` continua consumindo dela.
- **Limitações conhecidas:** a fila antiga não é removida automaticamente pelo Terraform (ele não apaga recurso que não gerencia); a função não expõe `/metrics` nesta fase (não é possível raspar uma função em repouso e o requisito de observabilidade já é atendido por users/catalog).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: remove o servico de notificacoes e documenta a funcao serverless"
```

---

### Task 4: Verificação final consolidada

**Files:**
- Create: `.superpowers/sdd/2026-09-14-sp4-serverless-keda/verify-sp4-final.ps1` (ledger, fora do git)

**Interfaces:**
- Consumes: tudo das Tasks 1-3.
- Produces: a evidência de aceite da spec (§143) e a lista de follow-ups.

- [ ] **Step 1: Escrever o verificador (ASCII puro, PS 5.1)**

Blocos obrigatórios:

```
V1  cluster: pods de mongo/redis/funcao ausente em repouso; ScaledObject READY=True; keda no ar
V2  filas: notifications-user-created / notifications-payment-processed / notifications-dead-letter existem;
            PaymentProcessed ainda com o catalog-api; UserCreated removida (limpeza)
V3  escala a zero: nenhum pod app=notifications-function antes do evento
V4  cadastro novo pelo gateway  -> KEDA cria pod -> log "[EMAIL ENVIADO] Boas-vindas para ..."
V5  compra pelo gateway (login + POST /api/jogos/{id}/comprar) -> log "[EMAIL ENVIADO] Confirmacao de compra para UserId: ..."
V6  volta a zero em ate ~1 min de cooldown -> nenhum pod da funcao
V7  DLQ: publicar payload invalido na fila -> apos as tentativas a mensagem aparece em notifications-dead-letter
V8  servico antigo ausente: sem deployment/pod notifications-api; manifesto removido do repo; compose sem o bloco
V9  terraform apply novamente -> "No changes" (idempotencia)
V10 SP1/SP2/SP3 intactos: gateway 401 sem token, /metrics das APIs, alvos do Prometheus up
```

Publicar um payload inválido para o V7 (controlador):

```powershell
kubectl exec deploy/rabbitmq -- rabbitmqadmin --username guest --password guest publish exchange=UserCreatedEvent routing_key="" payload='{'
```

**Atenção ao tipo de inválido:** um JSON **bem formado** com campos faltando **não** provoca falha — os campos ausentes viram `Guid.Empty`/string vazia e a função apenas loga valores vazios. Para exercitar a DLQ é preciso um JSON **malformado** (o `'{'` acima), que faz a desserialização lançar; a mensagem então volta para retry e é encaminhada para a DLX depois das tentativas. Registre no relatório o número de tentativas e o tempo observados até a mensagem chegar em `notifications-dead-letter`.

- [ ] **Step 2: Rodar e registrar**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .superpowers\sdd\2026-09-14-sp4-serverless-keda\verify-sp4-final.ps1 *> .superpowers\sdd\2026-09-14-sp4-serverless-keda\runtime-final.log
Get-Content .superpowers\sdd\2026-09-14-sp4-serverless-keda\runtime-final.log -Encoding Unicode
```

- [ ] **Step 3: Conferir o que não pode ter regredido**

```powershell
kubectl get pods
kubectl get pvc
curl.exe -s -o NUL -w '%{http_code}' http://localhost:18000/api/jogos   # 401 sem token
```

- [ ] **Step 4: Commit final (se a execução exigir ajuste de documentação)**

```bash
git add README.md
git commit -m "docs: ajusta a documentacao do serverless com o que a execucao mostrou"
```

---

## Auto-review deste plano

- **Cobertura da spec (§122-128):** repo novo com isolated worker + `RabbitMQTrigger` ✔ (Task 1); `Dockerfile`, `host.json`, `local.settings.example.json` e Terraform no próprio repositório ✔ (Task 1); função sem estado com retry/DLQ ✔ (Tasks 1 e 4); KEDA, remoção dos manifestos do container e do bloco do compose, README atualizado ✔ (Tasks 2 e 3); restrição de não alterar publicadores ✔ (Task 3 só remove o serviço antigo); verificação "evento de compra → log da função; 0 réplicas; container antigo ausente" ✔ (Task 4).
- **Placeholders:** os únicos valores a preencher na execução são a versão do KEDA (Step 1 da Task 2, com o endereço para conferir) e o `<sha7>` de cada build — ambos são valores descobertos no momento, não reticências de projeto. Nenhum `TBD` de conteúdo.
- **Consistência de tipos e nomes:** `NotificationService.EnviarBoasVindas(UserCreatedEvent)` e `EnviarConfirmacaoCompra(PaymentProcessedEvent)` são os mesmos nomes do serviço removido; os contratos `UserCreatedEvent`/`PaymentProcessedEvent` são cópias idênticas (mesmos campos e namespace) dos arquivos lidos em `fcg-notifications-api`; os nomes de fila usados nos atributos `[RabbitMQTrigger]` são exatamente os declarados no `terraform/broker.tf` (`notifications-user-created`, `notifications-payment-processed`); o `scaleTargetRef` do `ScaledObject` aponta para o nome do `Deployment` criado no mesmo arquivo Terraform.
- **Dependências entre tasks:** a Task 2 depende do veredito do Step 17 da Task 1 (Azurite ou não) e a Task 3 depende da função estar de pé com KEDA — a ordem está explícita no texto de cada uma.
- **Fora de escopo (registrado):** `/metrics` na função, idempotência persistida, unificação dos contratos de evento em pacote compartilhado, registry de imagens, alterar publicadores.
