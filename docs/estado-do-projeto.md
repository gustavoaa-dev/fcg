# Estado do Projeto — FCG (Fiap Cloud Games)

> Documento de memória/continuidade criado em 2026-08 por sessão de agente no DeepSeek Harness.
> Objetivo: registrar o entendimento do projeto, o que já foi feito e o que está planejado,
> para retomada rápida em sessões futuras.

## 1. Visão geral

**FCG — Fiap Cloud Games**: plataforma de gerenciamento de usuários, catálogo de jogos e
biblioteca pessoal de jogos, desenvolvida como trabalho de faculdade (FIAP). O projeto tem
**duas fases**:

- **Fase 1 — Monolito (código real deste repositório):** API REST em .NET 8, autenticação JWT
  com roles, EF Core + SQL Server.
- **Fase 2 — Microsserviços (referenciada, código em outros repositórios):** Users, Catalog,
  Payments, Notifications + orquestração (Docker/Kubernetes), comunicação assíncrona via
  RabbitMQ (`UserCreatedEvent`, `OrderPlacedEvent`, `PaymentProcessedEvent`).

**Estado local dos microsserviços:** as pastas `fcg-*` na raiz são **gitlinks** (ponteiros de
submodule, modo `160000`) **sem `.gitmodules` e sem checkout** — estão vazias no disco. O código
vive nos repositórios linkados no `README.md`. Link do Event Storming (Miro) também no README.

## 2. Fase 1 — Monolito: arquitetura

Layering clássico tipo *clean/layered*, com dependências apontando para dentro:

| Camada | Papel | Depende de |
|---|---|---|
| `FCG.API` | Controllers (`Auth`, `Users`, `Games`, `Biblioteca`), `Program.cs` (DI, JWT, Swagger, logs), `ErrorHandlingMiddleware` | Application + Infrastructure |
| `FCG.Application` | Serviços (`UserService`, `AuthService`, `GameService`) + DTOs — regras de negócio | Domain |
| `FCG.Domain` | Entidades (`User`, `Game`, `UserGame`), enum `UserRole`, **interfaces de repositório** | — (zero dependências) |
| `FCG.Infrastructure` | `FCGDbContext`, repositórios EF Core, migration `InitialCreate` | Domain |
| `FCG.Tests` | xUnit + Moq + FluentAssertions (presente no `FCG.sln`; ausente do `FCG.slnx`) | Application + Domain |

**Fluxo:** Controller → Service (valida → repositório → `Salvar()`) → Repository (EF Core) → SQL Server.

**Domínio:** `User` (Id Guid, Nome, Email, SenhaHash, Role, DataCadastro, coleção de jogos),
`Game` (Nome, Descrição, Preço `decimal(10,2)`), `UserGame` (junção com `DataCompra` — "compra"
= adicionar à biblioteca). Entidades com **setters privados** e construtores que centralizam
criação (Id + timestamps UTC gerados na entidade). Chave composta `UserId+GameId`; índice único
de Email.

**Segurança:** BCrypt para hash de senha; JWT HS256 com claims (`Id`, `Email`, `Nome`, `Role`
via `ClaimTypes.Role`); `[Authorize]` / `[Authorize(Roles = "Admin")]`.

**Endpoints:** `POST /api/auth/login`; `GET|POST /api/usuarios` (GET só Admin);
`GET|POST|DELETE /api/jogos` (POST/DELETE só Admin); `GET|POST /api/usuarios/{userId}/jogos`.

**Stack:** .NET 8, ASP.NET Core Web API, EF Core 8, SQL Server, JWT Bearer, BCrypt.Net-Next,
xUnit + Moq + FluentAssertions, Swashbuckle/OpenAPI.

## 3. Linha do tempo (git, branch `master`)

| Data | Commit | Entrega |
|---|---|---|
| 2026-04-22 | `da196d5` estrutura inicial | Solução .NET com 4 projetos |
| 2026-04-23 | `330ba8f` entidades + migration | `User`, `Game`, `UserGame`, `UserRole`, DbContext, migration `InitialCreate` |
| 2026-04-24 | `bad36fa` cadastro de usuários | Validações e-mail/senha + BCrypt |
| 2026-04-24 | `814fe43` autenticação JWT | Login, token, controle por role |
| 2026-04-28 | `e66b86f` CRUD jogos + biblioteca | `GamesController`, `BibliotecaController` |
| 2026-04-28 | `c84813e` middleware + Swagger | Erros global, logs, documentação |
| 2026-05-03 | `3c1fabd` testes TDD | Testes unitários (User, Email, Services) |
| 2026-05-04 | `3c97940` revisão + README | Ajustes gerais, warnings |
| 2026-07-09/13 | `72f2729`, `33bbc5d`, `c5b5c67` Fase 2 | Gitlinks `fcg-*` + README de microsserviços |

## 4. Modelo de pensamento do autor (convenções observadas)

- Código, mensagens e testes **em pt-BR**, nomes autoexplicativos
  (`CriarUsuario_EmailJaCadastrado_DeveLancarException`).
- **Validação em camadas:** data annotations (DTOs/ModelState) → validações estáticas no Domain
  (`User.EmailValido`, `User.SenhaValida`) → unicidade no banco (índice único).
- DDD-lite / Clean Architecture didática: domínio encapsulado, contratos de repositório no
  Domain, EF isolado na Infrastructure.
- Evolução incremental com Conventional Commits em pt-BR.
- TDD com Moq isolando serviços dos repositórios.
- QA manual via `testes-api.ps1` (smoke tests REST) e `testes-api-comandos.txt` (snippets).
- Design guiado por Event Storming (Miro) antes do código.

## 5. Pontos de atenção do monolito (candidatos a correção — sem evoluir features)

1. **Erros em dois padrões:** controllers devolvem `{ mensagem }` (BadRequest/NotFound/
   Unauthorized) e o `ErrorHandlingMiddleware` devolveria `ErroResponse` padronizado
   (`StatusCode/Mensagem/Detalhe/Timestamp`), mas quase nunca é exercitado (try/catch intercepta
   antes). Respostas de erro inconsistentes.
2. **Sem Admin criável pela API:** `CriarUsuario` sempre atribui `Role = Usuario`; não há seed —
   endpoints de Admin só funcionam com inserção manual no banco (scripts assumem
   `gustavo@email.com` / `Gustavo@123`).
3. **Biblioteca sem validação de usuário:** `POST /api/usuarios/{userId}/jogos` valida que o jogo
   existe, mas **não** valida que o usuário existe (falha por FK → 500) e qualquer autenticado
   pode adicionar jogo à biblioteca de outro usuário (sem checagem de ownership/role).
4. **Sem remoção da biblioteca:** não existe endpoint para remover um jogo da biblioteca do usuário.
5. **`IGameRepository.Atualizar` órfão:** existe no repositório mas não há PUT/PATCH de jogo
   (nenhum service/controller usa).
6. **Login:** mensagens distintas ("Usuário não encontrado." vs "Senha inválida.") permitem
   enumeração de usuários; controller converte tudo em 401.
7. **Middleware expõe `ex.StackTrace` em `Detalhe`** da resposta de erro.
8. **Resíduos de template:** `Class1.cs` em Application/Domain/Infrastructure, `UnitTest1.cs` em
   Tests, `launchUrl: weatherforecast` no `launchSettings.json`.
9. **Segredo JWT hardcoded** no `appsettings.json` versionado (mover para variável de ambiente/
   user-secrets em dev).

## 6. O que foi feito na sessão de análise (2026-08)

- Leitura integral do código-fonte do monolito e dos arquivos de apoio; consolidação do
  entendimento acima.
- Nenhuma alteração de código foi feita (working tree limpo).
- **Superpowers v6.3.0 instalado** (skills de metodologia de desenvolvimento) em nível de usuário:
  - Skills: `C:\Users\Gustavo\.dsh\skills\<nome>\SKILL.md` (14 skills).
  - Espelho do upstream p/ atualizações: `C:\Users\Gustavo\.dsh\superpowers`.
  - Verificado: 14 bundles, 0 inconsistentes; catálogo do DSH reconhece as skills.
  - Uso: pedir "use Superpowers" ou mencionar a skill; sem auto-bootstrap (DSH não roda hooks).

## 7. Próximos passos (decisão do usuário)

**Direção escolhida:** corrigir os **pontos de atenção do monolito (seção 5)**, sem evoluir
funcionalidades por enquanto. Fluxo: `brainstorming` (refinar escopo/design) → `writing-plans` →
execução com TDD (`test-driven-development`) + `verification-before-completion`.

### Como rodar/verificar (referência rápida)

- Migrations: `dotnet ef database update --project .\FCG.Infrastructure\FCG.Infrastructure.csproj --startup-project .\FCG.API\FCG.API.csproj`
- API: `dotnet run --project .\FCG.API\FCG.API.csproj` (Swagger em http://localhost:5071/swagger)
- Testes: `dotnet test .\FCG.sln` (ou `.\FCG.Tests\FCG.Tests.csproj`)
- Smoke tests manuais: `.\testes-api.ps1` (requer API rodando; assume usuário admin manual)

**2026-09-09:** direção aprovada; spec em `docs/superpowers/specs/2026-09-09-correcao-monolito-design.md` e plano de implementação em `docs/superpowers/plans/2026-09-09-correcao-monolito.md`. Estado: em implementação.
