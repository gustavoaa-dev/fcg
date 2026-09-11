# Spec — Correção dos Pontos de Atenção do Monolito FCG

- **Data:** 2026-09-09
- **Escopo:** correções estruturais no monolito (Fase 1) — **sem evoluir funcionalidades**
- **Processo:** brainstorming (Superpowers) — escopo, formato de erros, status HTTP, Admin, biblioteca, testes e segredo decididos com o usuário em 2026-09-09
- **Contexto:** ver `docs/estado-do-projeto.md` (seção 5 lista os 9 itens originais)

## Decisões aprovadas (registro)

| # | Tema | Decisão |
|---|---|---|
| D1 | Escopo | Todos os 9 itens de atenção em uma rodada |
| D2 | Estratégia de erros | Centralizar no `ErrorHandlingMiddleware` com exceções tipadas; controllers sem try/catch |
| D3 | Formato de erro | Manter `ErroResponse` e padronizar **todas** as respostas de erro nele (inclusive validação do ModelState); `Detalhe` (stacktrace) só em Development |
| D4 | Status HTTP | Semântica correta: 400 validação, 401 credenciais, 403 acesso negado, 404 não encontrado, 409 conflito, 500 genérico |
| D5 | Admin | Seed idempotente no startup, configurável (`AdminSeed:Email`/`Senha`), override por env |
| D6 | Biblioteca | Usuário comum acessa só a própria biblioteca; Admin acessa qualquer; POST valida usuário (404), jogo (404) e duplicidade (409) |
| D7 | DELETE biblioteca | Nova rota `DELETE /api/usuarios/{userId}/jogos/{gameId}` com a mesma regra de propriedade (D6) |
| D8 | `IGameRepository.Atualizar` | Remover método morto (YAGNI) |
| D9 | Testes | Unit tests (sem banco), incluindo middleware, ownership da biblioteca e seed; atualizar suíte existente aos novos contratos |
| D10 | Segredo JWT | Remover valor do `appsettings.json`; user-secrets em dev; variável de ambiente `Jwt__SecretKey` nos demais; fail-fast se ausente |

## Mapa item → mudança

| Item original | Mudança |
|---|---|
| 1. Erros em dois padrões | D2 + D3 + controllers enxutos |
| 2. Sem Admin pela API | D5 (seed configurável) |
| 3. Biblioteca sem validação/ownership | D4 + D6 + `IUserRepository` em `GameService` |
| 4. Sem remoção da biblioteca | D7 |
| 5. `Atualizar` órfão | D8 |
| 6. Login revela existência / tudo 401 | `CredenciaisInvalidasException` com mensagem única (D2/D4) |
| 7. Stacktrace no corpo de erro | D3 (Detalhe só em Development) |
| 8. Resíduos de template | Deletar `Class1.cs` (3x) e `UnitTest1.cs`; `launchUrl` → `swagger`; `FCG.slnx` ganha `FCG.Tests` |
| 9. Segredo JWT versionado | D10 |

## Design detalhado

### A. Exceções tipadas de negócio

Nova pasta `FCG.Application/Exceptions/` (namespace `FCG.Application.Exceptions`; 1 arquivo por exceção):

- `EntidadeNaoEncontradaException : Exception` → 404
- `ConflitoException : Exception` → 409
- `CredenciaisInvalidasException : Exception` → 401
- `AcessoNegadoException : Exception` → 403

Padrão: construtores com `message` (e inner exception opcional), seguindo o estilo do código atual (exceções simples com mensagem em pt-BR).

### B. `ErrorHandlingMiddleware` (API)

- Construtor passa a receber também `IHostEnvironment` (resolvido por DI no `UseMiddleware`).
- Mapa de status (switch):
  - `ArgumentException` (incl. `ArgumentNullException`) → **400**
  - `CredenciaisInvalidasException`, `UnauthorizedAccessException` → **401**
  - `AcessoNegadoException` → **403**
  - `EntidadeNaoEncontradaException`, `KeyNotFoundException` → **404**
  - `ConflitoException` → **409**
  - demais → **500**, mensagem genérica "Ocorreu um erro interno no servidor."
- `ErroResponse.Detalhe` = `ex.StackTrace` **somente quando `env.IsDevelopment()`**; senão `null`.
- Log: `LogError(ex, ...)` sempre (stacktrace completo no log, nunca no corpo em produção).
- Corpo de sucesso/erro: sempre `application/json` + `ErroResponse`.
- Código atual de resposta (mensagens) pode continuar vindo de `ex.Message` para as exceções conhecidas.

### C. Validação do ModelState padronizada (API)

- Em `Program.cs`, `builder.Services.Configure<ApiBehaviorOptions>(...)` com `InvalidModelStateResponseFactory` devolvendo 400 em formato `ErroResponse` (mensagens agregadas de `ModelState`), em vez do `ProblemDetails` automático do `[ApiController]`.

### D. Controllers enxutos

- Remover **todos** os `try/catch` de `AuthController`, `UsersController`, `GamesController`, `BibliotecaController`.
- Rotas e atributos `[Authorize]`/`[Authorize(Roles = "Admin")]` permanecem como estão, exceto o que a seção E define.
- `AuthController.Login`: sem try/catch; `CredenciaisInvalidasException` sobe ao middleware (401).

### E. Biblioteca e propriedade (API)

`BibliotecaController` (`[Authorize]`):

- Rotas: `GET /api/usuarios/{userId:guid}/jogos`, `POST /api/usuarios/{userId:guid}/jogos`, **nova** `DELETE /api/usuarios/{userId:guid}/jogos/{gameId:guid}`.
- Regra de propriedade: método privado no próprio controller, `VerificarAcesso(Guid userId)` — se `User` não tem role `Admin` **e** o claim `Id` (claim type `"Id"`, como emitido pelo `AuthService`) do token ≠ `userId` da rota → lança `AcessoNegadoException` (403). Implementação privada; testada indiretamente pelos `BibliotecaControllerTests` (que configuram `HttpContext.User`).
- Sem try/catch; sucesso: GET → 200, POST → 201, DELETE → 204.

`AuthService` (Application):

- Substituir `InvalidOperationException("Usuário não encontrado.")` e `UnauthorizedAccessException("Senha inválida.")` por `CredenciaisInvalidasException` com mensagem única: **"E-mail ou senha inválidos."**

`UserService.CriarUsuario` (Application):

- E-mail/senha inválidos → `ArgumentException` (mantém; 400).
- E-mail já cadastrado → `ConflitoException("Já existe um usuário cadastrado com este e-mail.")` (409).

`GameService` (Application) — novo construtor com `IUserRepository`:

- `AdicionarJogoAoUsuario(userId, gameId)`:
  - usuário inexistente → `EntidadeNaoEncontradaException` (404)
  - jogo inexistente → `EntidadeNaoEncontradaException` (404)
  - usuário já possui o jogo → `ConflitoException` (409)
- `ObterJogosDoUsuario(userId)`: usuário inexistente → `EntidadeNaoEncontradaException` (404); senão lista.
- Novo `RemoverJogoDoUsuario(userId, gameId)`:
  - usuário inexistente → 404
  - jogo não está na biblioteca do usuário → `EntidadeNaoEncontradaException` (404)
  - sucesso: remove `UserGame` + `Salvar()`

### F. Repositórios (Infrastructure)

- `IGameRepository`/`GameRepository`: remover `Atualizar(Game)`.
- `IUserGameRepository`/`UserGameRepository`: adicionar `Remover(UserGame)` (padrão atual: `_context.UserGames.Remove(...)`; sem `async` — `Task`/`Task.CompletedTask` como no `GameRepository.Remover`).

### G. Seed de Admin (API)

- Nova classe `FCG.API/Data/AdminSeed.cs` com método estático/testável (ex.: `Task EnsureCreatedAsync(IUserRepository userRepository, IConfiguration configuration)`) chamado no `Program.cs` após o build (escopo do `FCGDbContext` via `app.Services`).
- Comportamento (idempotente):
  - lê `AdminSeed:Email` e `AdminSeed:Senha`;
  - `Email` vazio/ausente → seed desabilitado;
  - se já existe usuário com o e-mail → nada;
  - senão cria `User(email, nome="Administrador", hash BCrypt, UserRole.Admin)` + `Adicionar` + `Salvar`.
- Config dev em `appsettings.json` (documentada como dev-only):
  ```json
  "AdminSeed": { "Email": "gustavo@email.com", "Senha": "Gustavo@123" }
  ```
  Override por `AdminSeed__Email` / `AdminSeed__Senha`.
- Observação: valores atuais mantêm os scripts `testes-api.ps1`/`testes-api-comandos.txt` funcionais (login admin e teste de e-mail duplicado).

### H. Segredo JWT (configuração)

- `FCG.API/appsettings.json`: remover a chave `Jwt:SecretKey` (manter `Issuer`, `Audience`, `ExpiracaoHoras`).
- `Program.cs`: o fail-fast existente (lança se `Jwt:SecretKey` ausente) já cobre os ambientes sem segredo.
- Dev: `dotnet user-secrets init --project FCG.API` + `dotnet user-secrets set "Jwt:SecretKey" "<valor>" --project FCG.API`.
- Demais: env `Jwt__SecretKey` (override automático da config .NET).
- **Atenção de execução:** `dotnet run` e `dotnet test` só funcionam após o segredo ser configurado em dev (user-secrets) — passos documentados no README e executados na implementação.

### I. Limpezas (item 8)

- Deletar: `FCG.Application/Class1.cs`, `FCG.Domain/Class1.cs`, `FCG.Infrastructure/Class1.cs`, `FCG.Tests/UnitTest1.cs`.
- `FCG.API/Properties/launchSettings.json`: `launchUrl` → `"swagger"` nos 3 profiles.
- `FCG.slnx`: adicionar `<Project Path="FCG.Tests/FCG.Tests.csproj" />`.

## Testes (unit, sem banco)

**Atualizar:**
- `FCG.Tests/Application/UserServiceTests.cs`: e-mail duplicado → `ConflitoException`; senha inválida → `ArgumentException`; caso feliz inalterado.
- `FCG.Tests/Application/GameServiceTests.cs`: novo construtor (`IUserRepository` mockado); casos: usuário inexistente (404), jogo inexistente (404), duplicado (409), feliz. Adicionar casos de `RemoverJogoDoUsuario` (sucesso + não está na biblioteca).

**Novos:**
- `FCG.Tests/API/ErrorHandlingMiddlewareTests.cs`:
  - tabela exceção → status esperado (400/401/403/404/409/500);
  - `Detalhe` presente com `IHostEnvironment` Development; `null` com Production;
  - exceção desconhecida → 500 com mensagem genérica;
  - usa `DefaultHttpContext` + `RequestDelegate`/`ILogger`/`IHostEnvironment` mockados (Moq).
- `FCG.Tests/API/BibliotecaControllerTests.cs`:
  - não-admin com `userId` diferente do claim → `AcessoNegadoException`;
  - admin com `userId` qualquer → flui normalmente;
  - caminhos felizes GET (200)/POST (201)/DELETE (204) com `GameService` real + repositórios mockados + `HttpContext.User` configurado;
  - 404/409 propagados como exceção (assert de `ThrowAsync`).
- `FCG.Tests/API/AdminSeedTests.cs`: idempotência (existe → não cria), criação com role Admin e senha hashada (via `IUserRepository` mockado + `BCrypt.Verify`), desabilitado com e-mail vazio.

**Execução:** `dotnet test .\FCG.sln` (e `FCG.slnx` após inclusão do projeto de testes). Sem banco/WebApplicationFactory.

## Documentação

- `README.md`:
  - tabela de endpoints: adicionar `DELETE /api/usuarios/{userId}/jogos/{gameId}`;
  - seção "Configuração": JWT via user-secrets (dev) / env (demais); `AdminSeed`; formato de erro padronizado;
  - remover o exemplo de `appsettings.json` com segredo hardcoded (ou marcar claramente como dev-only);
  - ajustar "Como rodar" para incluir os passos de user-secrets.
- `docs/estado-do-projeto.md`: nota final apontando para esta spec e o estado "em correção".

## Fora de escopo (explícito)

- Nenhuma funcionalidade nova de negócio: sem carrinho/pagamento, sem edição de jogo (`PUT`), sem promoção de role por endpoint, sem novos domínios.
- Sem testes de integração com `WebApplicationFactory` (decisão D9).
- Sem mudanças na Fase 2 (microsserviços) — as pastas `fcg-*` permanecem como estão.
