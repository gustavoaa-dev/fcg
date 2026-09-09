# FCG - Fiap Cloud Games

Plataforma de gerenciamento de usuários, catálogo de jogos e biblioteca pessoal.

## 🏗️ Arquitetura de Microsserviços (Fase 2)

A plataforma evoluiu para uma arquitetura de microsserviços orientada a eventos:

| Serviço | Repositório | Descrição |
|---|---|---|
| **UsersAPI** | [fcg-users-api](https://github.com/gustavoaa-dev/fcg-users-api) | Cadastro e autenticação JWT |
| **CatalogAPI** | [fcg-catalog-api](https://github.com/gustavoaa-dev/fcg-catalog-api) | Catálogo de jogos e biblioteca |
| **PaymentsAPI** | [fcg-payments-api](https://github.com/gustavoaa-dev/fcg-payments-api) | Processamento de pagamentos |
| **NotificationsAPI** | [fcg-notifications-api](https://github.com/gustavoaa-dev/fcg-notifications-api) | Envio de notificações |
| **Orquestração** | [fcg-orchestration](https://github.com/gustavoaa-dev/fcg-orchestration) | Docker Compose e Kubernetes |

Os microsserviços se comunicam via **RabbitMQ** utilizando eventos assíncronos (`UserCreatedEvent`, `OrderPlacedEvent`, `PaymentProcessedEvent`).

> Para instruções de execução com Docker e Kubernetes, consulte o [repositório de orquestração](https://github.com/gustavoaa-dev/fcg-orchestration).

---

## Monolito (Fase 1)

Este repositório contém a versão monolítica original do projeto, que serviu como base para a decomposição em microsserviços.

API REST em .NET 8 com autenticação JWT, controle de acesso por perfil e persistência em SQL Server.

### Estrutura

- `FCG.API` — camada de apresentação
- `FCG.Application` — serviços e DTOs
- `FCG.Domain` — entidades, enums e contratos
- `FCG.Infrastructure` — EF Core, repositórios e migrations
- `FCG.Tests` — testes automatizados

## Tecnologias utilizadas

- .NET 8
- ASP.NET Core Web API
- Entity Framework Core
- SQL Server
- JWT Bearer Authentication
- xUnit
- BCrypt
- Swagger / OpenAPI

## Pré-requisitos

Antes de executar o projeto, tenha instalado:

- .NET SDK 8
- SQL Server local ou acessível pela aplicação
- `dotnet-ef` para aplicar migrations

Instalação da ferramenta do Entity Framework, se necessário:

```powershell
dotnet tool install --global dotnet-ef
```

## Como rodar o projeto

### 1. Clonar o repositório

```powershell
git clone <URL_DO_REPOSITORIO>
cd FCG
```

### 2. Configurar o `appsettings.json`

O arquivo principal está em [FCG.API/appsettings.json](/C:/Users/GUSTAVO.ARAUJO/fcg/FCG.API/appsettings.json:1).

Exemplo da configuração atual:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=FCG;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "Jwt": {
    "Issuer": "FCG.API",
    "Audience": "FCG.Client",
    "ExpiracaoHoras": 8
  },
  "AdminSeed": {
    "Email": "gustavo@email.com",
    "Senha": "Gustavo@123"
  }
}
```

### 2.1 Chave secreta do JWT

O segredo do JWT **não fica no repositório**. Configure-o:

- **Desenvolvimento (user-secrets):**
  ```powershell
  dotnet user-secrets init --project .\FCG.API\FCG.API.csproj
  dotnet user-secrets set "Jwt:SecretKey" "<seu-segredo>" --project .\FCG.API\FCG.API.csproj
  ```
- **Outros ambientes (variável de ambiente):**
  ```powershell
  $env:Jwt__SecretKey = "<seu-segredo>"
  ```
A aplicação falha na inicialização se `Jwt:SecretKey` não estiver configurado.

### 2.2 Seed do usuário administrador

No primeiro start, a aplicação cria o administrador somente se **ambas** as configurações `AdminSeed:Email` e `AdminSeed:Senha` estiverem presentes e preenchidas — se `AdminSeed:Senha` estiver ausente ou vazio, o seed é desabilitado silenciosamente. Quando ativo, o usuário é criado apenas se ainda não existir outro com o mesmo e-mail de `AdminSeed:Email` (dev-only; sobrescreva via `AdminSeed__Email`/`AdminSeed__Senha` em produção).

### 3. Rodar as migrations

Se o banco ainda não existir ou precisar ser atualizado:

```powershell
dotnet ef database update --project .\FCG.Infrastructure\FCG.Infrastructure.csproj --startup-project .\FCG.API\FCG.API.csproj
```

### 4. Executar a API

```powershell
dotnet run --project .\FCG.API\FCG.API.csproj
```

URLs locais configuradas por padrão:

- `http://localhost:5071`
- `https://localhost:7265`

O Swagger fica disponível em:

- `http://localhost:5071/swagger`

## Endpoints disponíveis

| Método | Rota | Descrição | Autenticação |
|---|---|---|---|
| `POST` | `/api/auth/login` | Realiza login e retorna token JWT | Não |
| `GET` | `/api/usuarios` | Lista todos os usuários | Sim, `Admin` |
| `POST` | `/api/usuarios` | Cria um novo usuário | Não |
| `GET` | `/api/jogos` | Lista todos os jogos | Sim |
| `GET` | `/api/jogos/{id}` | Busca um jogo por ID | Sim |
| `POST` | `/api/jogos` | Cadastra um novo jogo | Sim, `Admin` |
| `DELETE` | `/api/jogos/{id}` | Remove um jogo | Sim, `Admin` |
| `GET` | `/api/usuarios/{userId}/jogos` | Lista a biblioteca de jogos do usuário | Sim |
| `POST` | `/api/usuarios/{userId}/jogos` | Adiciona um jogo à biblioteca do usuário | Sim |
| `DELETE` | `/api/usuarios/{userId}/jogos/{gameId}` | Remove um jogo da biblioteca do usuário | Sim |

> Erros produzidos pela aplicação (controllers, serviços e validação do ModelState) usam o formato `{ statusCode, mensagem, detalhe, timestamp }` (`detalhe` presente apenas em Development). Status: 400 validação, 401 credenciais inválidas, 403 acesso negado, 404 não encontrado, 409 conflito, 500 erro interno. Desafios de autenticação/autorização do framework (401/403) e rotas inexistentes mantêm o corpo padrão do ASP.NET Core.

## Como rodar os testes

Para executar todos os testes da solução:

```powershell
dotnet test .\FCG.sln
```

Para executar apenas o projeto de testes:

```powershell
dotnet test .\FCG.Tests\FCG.Tests.csproj
```

## Event Storming

Link do Event Storming no Miro:

https://miro.com/app/board/uXjVHYFhTrg=/
