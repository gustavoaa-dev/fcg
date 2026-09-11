# Correção dos Pontos de Atenção do Monolito FCG — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Corrigir os 9 pontos de atenção do monolito FCG (erros padronizados, exceções tipadas, biblioteca com ownership, seed de Admin, segredo JWT fora do repo, limpezas) sem evoluir funcionalidades.

**Architecture:** Erros centralizados num `ErrorHandlingMiddleware` que mapeia exceções de negócio tipadas (em `FCG.Application.Exceptions`) para status HTTP e corpo `ErroResponse` único; controllers ficam sem try/catch; `GameService` ganha validação de usuário e ownership na `BibliotecaController`; seed idempotente de Admin no startup; segredo JWT sai do `appsettings.json`.

**Tech Stack:** .NET 8 (net8.0), ASP.NET Core Web API, EF Core 8 + SQL Server, xUnit + FluentAssertions + Moq, BCrypt.Net-Next.

**Spec:** `docs/superpowers/specs/2026-09-09-correcao-monolito-design.md`

## Global Constraints

- TDD estrito: nenhum código de produção sem teste que falhou primeiro (ciclo RED → GREEN → REFACTOR por tarefa).
- Mensagens de exceção e nomes em pt-BR (padrão do repo). Commits em Conventional Commits pt-BR (`feat:`, `fix:`, `chore:`, `docs:`, `test:`).
- `Nullable` habilitado e `ImplicitUsings` habilitado em todos os projetos (não remover).
- Mapa de status: `ArgumentException`→400, `CredenciaisInvalidasException`/`UnauthorizedAccessException`→401, `AcessoNegadoException`→403, `EntidadeNaoEncontradaException`/`KeyNotFoundException`→404, `ConflitoException`→409, demais→500 (mensagem "Ocorreu um erro interno no servidor.").
- `ErroResponse.Detalhe` (stacktrace) presente **somente** em Development; `null` nos demais ambientes.
- Corpo de erro sempre no formato `ErroResponse` (nunca `ProblemDetails`/RFC 7807).
- Rotas e atributos `[Authorize]` existentes não mudam, exceto: nova rota `DELETE /api/usuarios/{userId}/jogos/{gameId}` e regra de propriedade da biblioteca.
- Sem testes de integração/WebApplicationFactory; sem banco nos testes (mocks).
- Rodar testes com: `dotnet test .\FCG.sln`. Rodar build com: `dotnet build .\FCG.sln`.
- Identidade git local do repo já configurada (Gustavo A. Araujo / gustavoaa-dev@users.noreply.github.com).

---

## Mapa de arquivos

**Criar (Application):**
- `FCG.Application/Exceptions/EntidadeNaoEncontradaException.cs`
- `FCG.Application/Exceptions/ConflitoException.cs`
- `FCG.Application/Exceptions/CredenciaisInvalidasException.cs`
- `FCG.Application/Exceptions/AcessoNegadoException.cs`

**Criar (API):**
- `FCG.API/Data/AdminSeed.cs`

**Criar (Tests):**
- `FCG.Tests/API/ErrorHandlingMiddlewareTests.cs`
- `FCG.Tests/API/BibliotecaControllerTests.cs`
- `FCG.Tests/API/AdminSeedTests.cs`
- `FCG.Tests/Application/AuthServiceTests.cs`

**Modificar:**
- `FCG.Application/Services/AuthService.cs`
- `FCG.Application/Services/UserService.cs`
- `FCG.Application/Services/GameService.cs`
- `FCG.Domain/Interfaces/IGameRepository.cs` (remove `Atualizar`)
- `FCG.Domain/Interfaces/IUserGameRepository.cs` (adiciona `Remover`)
- `FCG.Infrastructure/Repositories/GameRepository.cs` (remove `Atualizar`)
- `FCG.Infrastructure/Repositories/UserGameRepository.cs` (adiciona `Remover`)
- `FCG.API/Middlewares/ErrorHandlingMiddleware.cs`
- `FCG.API/Program.cs`
- `FCG.API/Controllers/AuthController.cs`
- `FCG.API/Controllers/UsersController.cs`
- `FCG.API/Controllers/GamesController.cs`
- `FCG.API/Controllers/BibliotecaController.cs`
- `FCG.API/appsettings.json`
- `FCG.API/Properties/launchSettings.json`
- `FCG.Tests/FCG.Tests.csproj` (referencia FCG.API)
- `FCG.Tests/Application/UserServiceTests.cs`
- `FCG.Tests/Application/GameServiceTests.cs` (reescrito)
- `FCG.slnx` (adiciona FCG.Tests)
- `README.md`
- `docs/estado-do-projeto.md`

**Deletar:**
- `FCG.Application/Class1.cs`
- `FCG.Domain/Class1.cs`
- `FCG.Infrastructure/Class1.cs`
- `FCG.Tests/UnitTest1.cs`

---

### Task 1: Exceções tipadas de negócio + middleware de erros padronizado

**Files:**
- Create: `FCG.Application/Exceptions/EntidadeNaoEncontradaException.cs`, `FCG.Application/Exceptions/ConflitoException.cs`, `FCG.Application/Exceptions/CredenciaisInvalidasException.cs`, `FCG.Application/Exceptions/AcessoNegadoException.cs`
- Modify: `FCG.API/Middlewares/ErrorHandlingMiddleware.cs` (substituição completa)
- Test: `FCG.Tests/API/ErrorHandlingMiddlewareTests.cs` (novo)

**Interfaces:**
- Produces: exceções `FCG.Application.Exceptions.{EntidadeNaoEncontradaException,ConflitoException,CredenciaisInvalidasException,AcessoNegadoException}` — todas `: Exception`, construtor `(string message)` e `(string message, Exception innerException)`.
- Produces: `ErrorHandlingMiddleware` com construtor `(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger, IHostEnvironment environment)`.

- [ ] **Step 1: Escrever o teste que falha (referencia exceções e middleware que ainda não existem)**

Create `FCG.Tests/API/ErrorHandlingMiddlewareTests.cs`:

```csharp
using System.Text.Json;
using FCG.API.Middlewares;
using FCG.Application.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;

namespace FCG.Tests.API;

public class ErrorHandlingMiddlewareTests
{
    public static IEnumerable<object[]> CasosDeMapeamento()
    {
        yield return new object[] { new ArgumentException("Dados inválidos."), StatusCodes.Status400BadRequest, "Dados inválidos." };
        yield return new object[] { new CredenciaisInvalidasException("Credenciais inválidas."), StatusCodes.Status401Unauthorized, "Credenciais inválidas." };
        yield return new object[] { new UnauthorizedAccessException("Não autorizado."), StatusCodes.Status401Unauthorized, "Não autorizado." };
        yield return new object[] { new AcessoNegadoException("Acesso negado."), StatusCodes.Status403Forbidden, "Acesso negado." };
        yield return new object[] { new EntidadeNaoEncontradaException("Jogo não encontrado."), StatusCodes.Status404NotFound, "Jogo não encontrado." };
        yield return new object[] { new KeyNotFoundException("Chave não encontrada."), StatusCodes.Status404NotFound, "Chave não encontrada." };
        yield return new object[] { new ConflitoException("Já existe."), StatusCodes.Status409Conflict, "Já existe." };
        yield return new object[] { new Exception("boom"), StatusCodes.Status500InternalServerError, "Ocorreu um erro interno no servidor." };
    }

    private static (int StatusCode, string Body) ExecutarComExcecao(Exception excecao, string environment = "Production")
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var loggerMock = new Mock<ILogger<ErrorHandlingMiddleware>>();
        var environmentMock = new Mock<IHostEnvironment>();
        environmentMock.Setup(e => e.EnvironmentName).Returns(environment);

        var middleware = new ErrorHandlingMiddleware(
            _ => { throw excecao; },
            loggerMock.Object,
            environmentMock.Object);

        middleware.InvokeAsync(context).GetAwaiter().GetResult();

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var body = reader.ReadToEnd();

        return (context.Response.StatusCode, body);
    }

    [Theory]
    [MemberData(nameof(CasosDeMapeamento))]
    public void Excecao_DeveRetornarStatusECorpoPadronizado(Exception excecao, int statusEsperado, string mensagemEsperada)
    {
        var (statusCode, body) = ExecutarComExcecao(excecao);

        statusCode.Should().Be(statusEsperado);
        var erro = JsonSerializer.Deserialize<ErroResponse>(body);
        erro!.StatusCode.Should().Be(statusEsperado);
        erro.Mensagem.Should().Be(mensagemEsperada);
        erro.Detalhe.Should().BeNull();
    }

    [Fact]
    public void AmbienteDevelopment_DeveIncluirDetalheComStackTrace()
    {
        var (statusCode, body) = ExecutarComExcecao(new InvalidOperationException("boom"), environment: "Development");

        statusCode.Should().Be(StatusCodes.Status500InternalServerError);
        var erro = JsonSerializer.Deserialize<ErroResponse>(body);
        erro!.Mensagem.Should().Be("Ocorreu um erro interno no servidor.");
        erro.Detalhe.Should().NotBeNullOrEmpty();
    }
}
```

- [ ] **Step 2: Rodar para ver falhar**

Run: `dotnet test .\FCG.Tests\FCG.Tests.csproj --filter "FullyQualifiedName~ErrorHandlingMiddlewareTests"`
Expected: falha de compilação — `FCG.Application.Exceptions` e o novo construtor do middleware não existem. (Falha de compilação = RED.)

- [ ] **Step 3: Implementar as exceções**

Create `FCG.Application/Exceptions/EntidadeNaoEncontradaException.cs`:

```csharp
namespace FCG.Application.Exceptions;

public class EntidadeNaoEncontradaException : Exception
{
    public EntidadeNaoEncontradaException(string message) : base(message) { }
    public EntidadeNaoEncontradaException(string message, Exception innerException) : base(message, innerException) { }
}
```

Create `FCG.Application/Exceptions/ConflitoException.cs`:

```csharp
namespace FCG.Application.Exceptions;

public class ConflitoException : Exception
{
    public ConflitoException(string message) : base(message) { }
    public ConflitoException(string message, Exception innerException) : base(message, innerException) { }
}
```

Create `FCG.Application/Exceptions/CredenciaisInvalidasException.cs`:

```csharp
namespace FCG.Application.Exceptions;

public class CredenciaisInvalidasException : Exception
{
    public CredenciaisInvalidasException(string message) : base(message) { }
    public CredenciaisInvalidasException(string message, Exception innerException) : base(message, innerException) { }
}
```

Create `FCG.Application/Exceptions/AcessoNegadoException.cs`:

```csharp
namespace FCG.Application.Exceptions;

public class AcessoNegadoException : Exception
{
    public AcessoNegadoException(string message) : base(message) { }
    public AcessoNegadoException(string message, Exception innerException) : base(message, innerException) { }
}
```

- [ ] **Step 4: Reescrever o middleware**

Replace the whole content of `FCG.API/Middlewares/ErrorHandlingMiddleware.cs` with:

```csharp
using System.Text.Json;
using FCG.Application.Exceptions;

namespace FCG.API.Middlewares;

/// <summary>
/// Middleware responsável por capturar exceções não tratadas e retornar respostas JSON padronizadas.
/// </summary>
public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    /// <summary>
    /// Inicializa uma nova instância do middleware de tratamento global de erros.
    /// </summary>
    public ErrorHandlingMiddleware(
        RequestDelegate next,
        ILogger<ErrorHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    /// <summary>
    /// Processa a requisição HTTP e intercepta exceções para convertê-las em respostas JSON padronizadas.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro não tratado durante o processamento da requisição.");

            var (statusCode, mensagem) = ex switch
            {
                ArgumentException => (StatusCodes.Status400BadRequest, ex.Message),
                CredenciaisInvalidasException or UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, ex.Message),
                AcessoNegadoException => (StatusCodes.Status403Forbidden, ex.Message),
                EntidadeNaoEncontradaException or KeyNotFoundException => (StatusCodes.Status404NotFound, ex.Message),
                ConflitoException => (StatusCodes.Status409Conflict, ex.Message),
                _ => (StatusCodes.Status500InternalServerError, "Ocorreu um erro interno no servidor.")
            };

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = statusCode;

            var erroResponse = new ErroResponse
            {
                StatusCode = statusCode,
                Mensagem = mensagem,
                Detalhe = _environment.IsDevelopment() ? ex.StackTrace : null
            };

            var json = JsonSerializer.Serialize(erroResponse);
            await context.Response.WriteAsync(json);
        }
    }
}
```

- [ ] **Step 5: Rodar os testes para ver passar**

Run: `dotnet test .\FCG.Tests\FCG.Tests.csproj --filter "FullyQualifiedName~ErrorHandlingMiddlewareTests"`
Expected: PASS (9 testes: 8 da teoria + 1 de Development). Rodar também `dotnet build .\FCG.sln` — deve compilar.

- [ ] **Step 6: Commit**

```bash
git add FCG.Application/Exceptions FCG.API/Middlewares/ErrorHandlingMiddleware.cs FCG.Tests/API/ErrorHandlingMiddlewareTests.cs
git commit -m "feat: excecoes de negocio tipadas e middleware de erros padronizado"
```

---

### Task 2: AuthService e UserService com exceções tipadas (401 uniforme e 409)

**Files:**
- Modify: `FCG.Application/Services/AuthService.cs`, `FCG.Application/Services/UserService.cs`
- Test: `FCG.Tests/Application/AuthServiceTests.cs` (novo), `FCG.Tests/Application/UserServiceTests.cs`

**Interfaces:**
- Consumes: `CredenciaisInvalidasException`, `ConflitoException` (Task 1).
- Produces: `AuthService.GerarToken` lança `CredenciaisInvalidasException("E-mail ou senha inválidos.")` para usuário inexistente **ou** senha incorreta. `UserService.CriarUsuario` lança `ConflitoException("Já existe um usuário cadastrado com este e-mail.")` para e-mail duplicado.

- [ ] **Step 1: Escrever os testes que falham**

Create `FCG.Tests/Application/AuthServiceTests.cs`:

```csharp
using FCG.Application.DTOs;
using FCG.Application.Exceptions;
using FCG.Application.Services;
using FCG.Domain.Entities;
using FCG.Domain.Enums;
using FCG.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace FCG.Tests.Application;

public class AuthServiceTests
{
    [Fact]
    public async Task GerarToken_UsuarioInexistente_DeveLancarCredenciaisInvalidas()
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        userRepositoryMock.Setup(repository => repository.ObterPorEmail("naoexiste@fcg.com"))
            .ReturnsAsync((User?)null);

        var service = new AuthService(userRepositoryMock.Object, Mock.Of<Microsoft.Extensions.Configuration.IConfiguration>());
        var dto = new LoginDTO { Email = "naoexiste@fcg.com", Senha = "Qualquer@1" };

        var act = async () => await service.GerarToken(dto);

        await act.Should().ThrowAsync<CredenciaisInvalidasException>()
            .WithMessage("E-mail ou senha inválidos.");
    }

    [Fact]
    public async Task GerarToken_SenhaIncorreta_DeveLancarCredenciaisInvalidas()
    {
        var senhaHash = BCrypt.Net.BCrypt.HashPassword("SenhaCorreta@1");
        var user = new User("Usuario", "usuario@fcg.com", senhaHash, UserRole.Usuario);

        var userRepositoryMock = new Mock<IUserRepository>();
        userRepositoryMock.Setup(repository => repository.ObterPorEmail("usuario@fcg.com"))
            .ReturnsAsync(user);

        var service = new AuthService(userRepositoryMock.Object, Mock.Of<Microsoft.Extensions.Configuration.IConfiguration>());
        var dto = new LoginDTO { Email = "usuario@fcg.com", Senha = "SenhaErrada@1" };

        var act = async () => await service.GerarToken(dto);

        await act.Should().ThrowAsync<CredenciaisInvalidasException>()
            .WithMessage("E-mail ou senha inválidos.");
    }
}
```

Update `FCG.Tests/Application/UserServiceTests.cs`:
- Add `using FCG.Application.Exceptions;`
- In `CriarUsuario_EmailJaCadastrado_DeveLancarException`, replace the assertion `await act.Should().ThrowAsync<InvalidOperationException>();` with `await act.Should().ThrowAsync<ConflitoException>();`.

- [ ] **Step 2: Rodar para ver falhar**

Run: `dotnet test .\FCG.Tests\FCG.Tests.csproj --filter "FullyQualifiedName~AuthServiceTests|FullyQualifiedName~UserServiceTests"`
Expected: RED — `AuthServiceTests` falha ("Usuário não encontrado." é `InvalidOperationException`, não `CredenciaisInvalidasException`) e `UserServiceTests` falha (espera `ConflitoException`, recebe `InvalidOperationException`).

- [ ] **Step 3: Implementar**

In `FCG.Application/Services/AuthService.cs`:
- Add `using FCG.Application.Exceptions;`
- Replace:

```csharp
        var user = await _userRepository.ObterPorEmail(dto.Email);
        if (user is null)
            throw new InvalidOperationException("Usuário não encontrado.");

        var senhaValida = BCrypt.Net.BCrypt.Verify(dto.Senha, user.SenhaHash);
        if (!senhaValida)
            throw new UnauthorizedAccessException("Senha inválida.");
```

with:

```csharp
        var user = await _userRepository.ObterPorEmail(dto.Email);
        if (user is null)
            throw new CredenciaisInvalidasException("E-mail ou senha inválidos.");

        var senhaValida = BCrypt.Net.BCrypt.Verify(dto.Senha, user.SenhaHash);
        if (!senhaValida)
            throw new CredenciaisInvalidasException("E-mail ou senha inválidos.");
```

In `FCG.Application/Services/UserService.cs`:
- Add `using FCG.Application.Exceptions;`
- Replace:

```csharp
        var usuarioExistente = await _userRepository.ObterPorEmail(dto.Email);
        if (usuarioExistente is not null)
            throw new InvalidOperationException("Já existe um usuário cadastrado com este e-mail.");
```

with:

```csharp
        var usuarioExistente = await _userRepository.ObterPorEmail(dto.Email);
        if (usuarioExistente is not null)
            throw new ConflitoException("Já existe um usuário cadastrado com este e-mail.");
```

- [ ] **Step 4: Rodar para ver passar**

Run: `dotnet test .\FCG.sln`
Expected: PASS (suíte inteira: middleware + auth + user + demais existentes).

- [ ] **Step 5: Commit**

```bash
git add FCG.Application/Services/AuthService.cs FCG.Application/Services/UserService.cs FCG.Tests/Application/AuthServiceTests.cs FCG.Tests/Application/UserServiceTests.cs
git commit -m "feat: autenticacao e cadastro com excecoes tipadas (401 uniforme e 409)"
```

---

### Task 3: GameService com validações (404/409), RemoverJogoDoUsuario e limpeza do Atualizar

**Files:**
- Modify: `FCG.Application/Services/GameService.cs`, `FCG.Domain/Interfaces/IGameRepository.cs`, `FCG.Domain/Interfaces/IUserGameRepository.cs`, `FCG.Infrastructure/Repositories/GameRepository.cs`, `FCG.Infrastructure/Repositories/UserGameRepository.cs`
- Test: `FCG.Tests/Application/GameServiceTests.cs` (reescrito)

**Interfaces:**
- Consumes: `EntidadeNaoEncontradaException`, `ConflitoException`; `IUserRepository` (existente em `FCG.Domain.Interfaces`).
- Produces: `GameService` com construtor `(IGameRepository, IUserGameRepository, IUserRepository)`; métodos que mudam de contrato de exceção: `ObterPorId`, `Remover`, `AdicionarJogoAoUsuario`, `ObterJogosDoUsuario`; novo método `Task RemoverJogoDoUsuario(Guid userId, Guid gameId)`. `IUserGameRepository.Remover(UserGame userGame)` novo; `IGameRepository.Atualizar(Game)` removido.

- [ ] **Step 1: Reescrever o teste (RED)**

Replace the whole content of `FCG.Tests/Application/GameServiceTests.cs` with:

```csharp
using FCG.Application.DTOs;
using FCG.Application.Exceptions;
using FCG.Application.Services;
using FCG.Domain.Entities;
using FCG.Domain.Enums;
using FCG.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace FCG.Tests.Application;

public class GameServiceTests
{
    private static User CriarUsuario(Guid id) => new("Usuario", "usuario@fcg.com", "hash", UserRole.Usuario);

    private static Game CriarGame() => new("Game Teste", "Descricao", 99.90m);

    private static (GameService Service, Mock<IGameRepository> Games, Mock<IUserGameRepository> UserGames, Mock<IUserRepository> Users) CriarService()
    {
        var games = new Mock<IGameRepository>();
        var userGames = new Mock<IUserGameRepository>();
        var users = new Mock<IUserRepository>();
        var service = new GameService(games.Object, userGames.Object, users.Object);
        return (service, games, userGames, users);
    }

    [Fact]
    public async Task AdicionarJogoAoUsuario_UsuarioInexistente_DeveLancarEntidadeNaoEncontrada()
    {
        var (service, _, _, users) = CriarService();
        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        users.Setup(repository => repository.ObterPorId(userId)).ReturnsAsync((User?)null);

        var act = async () => await service.AdicionarJogoAoUsuario(userId, gameId);

        await act.Should().ThrowAsync<EntidadeNaoEncontradaException>();
    }

    [Fact]
    public async Task AdicionarJogoAoUsuario_JogoInexistente_DeveLancarEntidadeNaoEncontrada()
    {
        var (service, games, _, users) = CriarService();
        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        users.Setup(repository => repository.ObterPorId(userId)).ReturnsAsync(CriarUsuario(userId));
        games.Setup(repository => repository.ObterPorId(gameId)).ReturnsAsync((Game?)null);

        var act = async () => await service.AdicionarJogoAoUsuario(userId, gameId);

        await act.Should().ThrowAsync<EntidadeNaoEncontradaException>();
    }

    [Fact]
    public async Task AdicionarJogoAoUsuario_UsuarioJaPossuiJogo_DeveLancarConflito()
    {
        var (service, games, userGames, users) = CriarService();
        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        users.Setup(repository => repository.ObterPorId(userId)).ReturnsAsync(CriarUsuario(userId));
        games.Setup(repository => repository.ObterPorId(gameId)).ReturnsAsync(CriarGame());
        userGames.Setup(repository => repository.ObterPorIds(userId, gameId)).ReturnsAsync(new UserGame(userId, gameId));

        var act = async () => await service.AdicionarJogoAoUsuario(userId, gameId);

        await act.Should().ThrowAsync<ConflitoException>();
    }

    [Fact]
    public async Task AdicionarJogoAoUsuario_DadosValidos_DeveAdicionarNaBiblioteca()
    {
        var (service, games, userGames, users) = CriarService();
        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        users.Setup(repository => repository.ObterPorId(userId)).ReturnsAsync(CriarUsuario(userId));
        games.Setup(repository => repository.ObterPorId(gameId)).ReturnsAsync(CriarGame());
        userGames.Setup(repository => repository.ObterPorIds(userId, gameId)).ReturnsAsync((UserGame?)null);

        await service.AdicionarJogoAoUsuario(userId, gameId);

        userGames.Verify(repository => repository.Adicionar(It.Is<UserGame>(userGame =>
            userGame.UserId == userId && userGame.GameId == gameId)), Times.Once);
        userGames.Verify(repository => repository.Salvar(), Times.Once);
    }

    [Fact]
    public async Task ObterJogosDoUsuario_UsuarioInexistente_DeveLancarEntidadeNaoEncontrada()
    {
        var (service, _, _, users) = CriarService();
        var userId = Guid.NewGuid();
        users.Setup(repository => repository.ObterPorId(userId)).ReturnsAsync((User?)null);

        var act = async () => await service.ObterJogosDoUsuario(userId);

        await act.Should().ThrowAsync<EntidadeNaoEncontradaException>();
    }

    [Fact]
    public async Task ObterJogosDoUsuario_UsuarioSemJogos_DeveRetornarListaVazia()
    {
        var (service, _, userGames, users) = CriarService();
        var userId = Guid.NewGuid();
        users.Setup(repository => repository.ObterPorId(userId)).ReturnsAsync(CriarUsuario(userId));
        userGames.Setup(repository => repository.ObterJogosDoUsuario(userId)).ReturnsAsync(new List<UserGame>());

        var resultado = await service.ObterJogosDoUsuario(userId);

        resultado.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoverJogoDoUsuario_UsuarioInexistente_DeveLancarEntidadeNaoEncontrada()
    {
        var (service, _, _, users) = CriarService();
        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        users.Setup(repository => repository.ObterPorId(userId)).ReturnsAsync((User?)null);

        var act = async () => await service.RemoverJogoDoUsuario(userId, gameId);

        await act.Should().ThrowAsync<EntidadeNaoEncontradaException>();
    }

    [Fact]
    public async Task RemoverJogoDoUsuario_JogoNaoEstaNaBiblioteca_DeveLancarEntidadeNaoEncontrada()
    {
        var (service, _, userGames, users) = CriarService();
        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        users.Setup(repository => repository.ObterPorId(userId)).ReturnsAsync(CriarUsuario(userId));
        userGames.Setup(repository => repository.ObterPorIds(userId, gameId)).ReturnsAsync((UserGame?)null);

        var act = async () => await service.RemoverJogoDoUsuario(userId, gameId);

        await act.Should().ThrowAsync<EntidadeNaoEncontradaException>();
    }

    [Fact]
    public async Task RemoverJogoDoUsuario_DadosValidos_DeveRemoverDaBiblioteca()
    {
        var (service, _, userGames, users) = CriarService();
        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var userGame = new UserGame(userId, gameId);
        users.Setup(repository => repository.ObterPorId(userId)).ReturnsAsync(CriarUsuario(userId));
        userGames.Setup(repository => repository.ObterPorIds(userId, gameId)).ReturnsAsync(userGame);

        await service.RemoverJogoDoUsuario(userId, gameId);

        userGames.Verify(repository => repository.Remover(userGame), Times.Once);
        userGames.Verify(repository => repository.Salvar(), Times.Once);
    }

    [Fact]
    public async Task ObterPorId_JogoInexistente_DeveLancarEntidadeNaoEncontrada()
    {
        var (service, games, _, _) = CriarService();
        var gameId = Guid.NewGuid();
        games.Setup(repository => repository.ObterPorId(gameId)).ReturnsAsync((Game?)null);

        var act = async () => await service.ObterPorId(gameId);

        await act.Should().ThrowAsync<EntidadeNaoEncontradaException>();
    }
}
```

Note: `using FCG.Application.DTOs;` é necessário porque `ObterJogosDoUsuario` retorna `IEnumerable<GameResponseDTO>`.

- [ ] **Step 2: Rodar para ver falhar**

Run: `dotnet test .\FCG.Tests\FCG.Tests.csproj --filter "FullyQualifiedName~GameServiceTests"`
Expected: RED — falha de compilação (construtor com 2 argumentos não existe mais; `Remover`/`RemoverJogoDoUsuario` ausentes) e/ou asserções de exceção incorretas.

- [ ] **Step 3: Implementar**

Update `FCG.Domain/Interfaces/IGameRepository.cs` — replace the whole file with:

```csharp
using FCG.Domain.Entities;

namespace FCG.Domain.Interfaces;

public interface IGameRepository
{
    Task<Game?> ObterPorId(Guid id);
    Task<IEnumerable<Game>> ObterTodos();
    Task Adicionar(Game game);
    Task Remover(Game game);
    Task Salvar();
}
```

Update `FCG.Domain/Interfaces/IUserGameRepository.cs` — replace the whole file with:

```csharp
using FCG.Domain.Entities;

namespace FCG.Domain.Interfaces;

public interface IUserGameRepository
{
    Task<IEnumerable<UserGame>> ObterJogosDoUsuario(Guid userId);
    Task<UserGame?> ObterPorIds(Guid userId, Guid gameId);
    Task Adicionar(UserGame userGame);
    Task Remover(UserGame userGame);
    Task Salvar();
}
```

Update `FCG.Infrastructure/Repositories/GameRepository.cs` — delete the `Atualizar` method (the block between `Adicionar` and `Remover`):

```csharp
    public Task Atualizar(Game game)
    {
        _context.Games.Update(game);
        return Task.CompletedTask;
    }

```

Update `FCG.Infrastructure/Repositories/UserGameRepository.cs` — add after `Adicionar`:

```csharp
    public Task Remover(UserGame userGame)
    {
        _context.UserGames.Remove(userGame);
        return Task.CompletedTask;
    }

```

Update `FCG.Application/Services/GameService.cs`:
- Add `using FCG.Application.Exceptions;`
- Replace the constructor:

```csharp
    private readonly IGameRepository _gameRepository;
    private readonly IUserGameRepository _userGameRepository;

    public GameService(IGameRepository gameRepository, IUserGameRepository userGameRepository)
    {
        _gameRepository = gameRepository;
        _userGameRepository = userGameRepository;
    }
```

with:

```csharp
    private readonly IGameRepository _gameRepository;
    private readonly IUserGameRepository _userGameRepository;
    private readonly IUserRepository _userRepository;

    public GameService(IGameRepository gameRepository, IUserGameRepository userGameRepository, IUserRepository userRepository)
    {
        _gameRepository = gameRepository;
        _userGameRepository = userGameRepository;
        _userRepository = userRepository;
    }
```

- Replace `ObterPorId` (throw):

```csharp
        var game = await _gameRepository.ObterPorId(id);
        if (game is null)
            throw new InvalidOperationException("Jogo não encontrado.");
```

with:

```csharp
        var game = await _gameRepository.ObterPorId(id);
        if (game is null)
            throw new EntidadeNaoEncontradaException("Jogo não encontrado.");
```

- Replace `Remover` (throw):

```csharp
        var game = await _gameRepository.ObterPorId(id);
        if (game is null)
            throw new InvalidOperationException("Jogo não encontrado.");
```

with:

```csharp
        var game = await _gameRepository.ObterPorId(id);
        if (game is null)
            throw new EntidadeNaoEncontradaException("Jogo não encontrado.");
```

- Replace `AdicionarJogoAoUsuario` with:

```csharp
    public async Task AdicionarJogoAoUsuario(Guid userId, Guid gameId)
    {
        var usuario = await _userRepository.ObterPorId(userId);
        if (usuario is null)
            throw new EntidadeNaoEncontradaException("Usuário não encontrado.");

        var game = await _gameRepository.ObterPorId(gameId);
        if (game is null)
            throw new EntidadeNaoEncontradaException("Jogo não encontrado.");

        var userGameExistente = await _userGameRepository.ObterPorIds(userId, gameId);
        if (userGameExistente is not null)
            throw new ConflitoException("O usuário já possui este jogo.");

        var userGame = new UserGame(userId, gameId);

        await _userGameRepository.Adicionar(userGame);
        await _userGameRepository.Salvar();
    }
```

- Replace `ObterJogosDoUsuario` with:

```csharp
    public async Task<IEnumerable<GameResponseDTO>> ObterJogosDoUsuario(Guid userId)
    {
        var usuario = await _userRepository.ObterPorId(userId);
        if (usuario is null)
            throw new EntidadeNaoEncontradaException("Usuário não encontrado.");

        var userGames = await _userGameRepository.ObterJogosDoUsuario(userId);
        return userGames.Select(userGame => MapearParaResponse(userGame.Game)).ToList();
    }
```

- Add the new method after `ObterJogosDoUsuario`:

```csharp
    public async Task RemoverJogoDoUsuario(Guid userId, Guid gameId)
    {
        var usuario = await _userRepository.ObterPorId(userId);
        if (usuario is null)
            throw new EntidadeNaoEncontradaException("Usuário não encontrado.");

        var userGame = await _userGameRepository.ObterPorIds(userId, gameId);
        if (userGame is null)
            throw new EntidadeNaoEncontradaException("O jogo não está na biblioteca do usuário.");

        await _userGameRepository.Remover(userGame);
        await _userGameRepository.Salvar();
    }
```

- [ ] **Step 4: Rodar para ver passar**

Run: `dotnet test .\FCG.sln`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add FCG.Application/Services/GameService.cs FCG.Domain/Interfaces FCG.Infrastructure/Repositories FCG.Tests/Application/GameServiceTests.cs
git commit -m "feat: validacoes de biblioteca com 404/409, remocao de jogo da biblioteca e limpeza de metodo morto"
```

---

### Task 4: BibliotecaController com ownership e rota DELETE (controllers sem try/catch)

**Files:**
- Modify: `FCG.API/Controllers/BibliotecaController.cs` (substituição completa)
- Test: `FCG.Tests/API/BibliotecaControllerTests.cs` (novo)

**Interfaces:**
- Consumes: `GameService` novo construtor e `RemoverJogoDoUsuario` (Task 3); `AcessoNegadoException`; claim `"Id"` (como emitido pelo `AuthService`).
- Produces: rotas `GET /`, `POST /`, `DELETE /{gameId:guid}` sob `/api/usuarios/{userId:guid}/jogos`; método privado `VerificarAcesso(Guid userId)`.

- [ ] **Step 1: Escrever o teste que falha**

Create `FCG.Tests/API/BibliotecaControllerTests.cs`:

```csharp
using System.Security.Claims;
using FCG.API.Controllers;
using FCG.Application.DTOs;
using FCG.Application.Exceptions;
using FCG.Application.Services;
using FCG.Domain.Entities;
using FCG.Domain.Enums;
using FCG.Domain.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace FCG.Tests.API;

public class BibliotecaControllerTests
{
    private static User CriarUsuario(Guid id, UserRole role = UserRole.Usuario) =>
        new("Usuario", "usuario@fcg.com", "hash", role);

    private static Game CriarGame() => new("Game Teste", "Descricao", 99.90m);

    private static ClaimsPrincipal CriarPrincipal(Guid id, bool admin = false)
    {
        var claims = new List<Claim> { new("Id", id.ToString()) };
        if (admin)
            claims.Add(new Claim(ClaimTypes.Role, UserRole.Admin.ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Teste"));
    }

    private static (BibliotecaController Controller, Mock<IGameRepository> Games, Mock<IUserGameRepository> UserGames, Mock<IUserRepository> Users) CriarController(ClaimsPrincipal principal)
    {
        var games = new Mock<IGameRepository>();
        var userGames = new Mock<IUserGameRepository>();
        var users = new Mock<IUserRepository>();
        var service = new GameService(games.Object, userGames.Object, users.Object);
        var controller = new BibliotecaController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            }
        };
        return (controller, games, userGames, users);
    }

    [Fact]
    public async Task Get_UsuarioNaoAdminEmBibliotecaAlheia_DeveLancarAcessoNegado()
    {
        var donoId = Guid.NewGuid();
        var invasorId = Guid.NewGuid();
        var (controller, _, _, _) = CriarController(CriarPrincipal(invasorId));

        var act = async () => await controller.Get(donoId);

        await act.Should().ThrowAsync<AcessoNegadoException>();
    }

    [Fact]
    public async Task Post_UsuarioNaoAdminEmBibliotecaAlheia_DeveLancarAcessoNegado()
    {
        var donoId = Guid.NewGuid();
        var invasorId = Guid.NewGuid();
        var (controller, _, _, _) = CriarController(CriarPrincipal(invasorId));

        var act = async () => await controller.Post(donoId, new AdicionarJogoUsuarioDTO { GameId = Guid.NewGuid() });

        await act.Should().ThrowAsync<AcessoNegadoException>();
    }

    [Fact]
    public async Task Get_DonoDaBiblioteca_DeveRetornarLista()
    {
        var userId = Guid.NewGuid();
        var (controller, _, userGames, users) = CriarController(CriarPrincipal(userId));
        users.Setup(repository => repository.ObterPorId(userId)).ReturnsAsync(CriarUsuario(userId));
        userGames.Setup(repository => repository.ObterJogosDoUsuario(userId)).ReturnsAsync(new List<UserGame>());

        var resultado = await controller.Get(userId);

        resultado.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Get_AdminEmBibliotecaDeOutroUsuario_DeveRetornarLista()
    {
        var adminId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (controller, _, userGames, users) = CriarController(CriarPrincipal(adminId, admin: true));
        users.Setup(repository => repository.ObterPorId(userId)).ReturnsAsync(CriarUsuario(userId));
        userGames.Setup(repository => repository.ObterJogosDoUsuario(userId)).ReturnsAsync(new List<UserGame>());

        var resultado = await controller.Get(userId);

        resultado.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Get_UsuarioInexistente_DeveLancarEntidadeNaoEncontrada()
    {
        var userId = Guid.NewGuid();
        var (controller, _, _, users) = CriarController(CriarPrincipal(userId));
        users.Setup(repository => repository.ObterPorId(userId)).ReturnsAsync((User?)null);

        var act = async () => await controller.Get(userId);

        await act.Should().ThrowAsync<EntidadeNaoEncontradaException>();
    }

    [Fact]
    public async Task Post_DadosValidos_DeveRetornarCreated()
    {
        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var (controller, games, userGames, users) = CriarController(CriarPrincipal(userId));
        users.Setup(repository => repository.ObterPorId(userId)).ReturnsAsync(CriarUsuario(userId));
        games.Setup(repository => repository.ObterPorId(gameId)).ReturnsAsync(CriarGame());
        userGames.Setup(repository => repository.ObterPorIds(userId, gameId)).ReturnsAsync((UserGame?)null);

        var resultado = await controller.Post(userId, new AdicionarJogoUsuarioDTO { GameId = gameId });

        resultado.Should().BeOfType<CreatedResult>();
        userGames.Verify(repository => repository.Adicionar(It.IsAny<UserGame>()), Times.Once);
        userGames.Verify(repository => repository.Salvar(), Times.Once);
    }

    [Fact]
    public async Task Post_JogoDuplicado_DeveLancarConflito()
    {
        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var (controller, games, userGames, users) = CriarController(CriarPrincipal(userId));
        users.Setup(repository => repository.ObterPorId(userId)).ReturnsAsync(CriarUsuario(userId));
        games.Setup(repository => repository.ObterPorId(gameId)).ReturnsAsync(CriarGame());
        userGames.Setup(repository => repository.ObterPorIds(userId, gameId)).ReturnsAsync(new UserGame(userId, gameId));

        var act = async () => await controller.Post(userId, new AdicionarJogoUsuarioDTO { GameId = gameId });

        await act.Should().ThrowAsync<ConflitoException>();
    }

    [Fact]
    public async Task Delete_DadosValidos_DeveRetornarNoContent()
    {
        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var userGame = new UserGame(userId, gameId);
        var (controller, _, userGames, users) = CriarController(CriarPrincipal(userId));
        users.Setup(repository => repository.ObterPorId(userId)).ReturnsAsync(CriarUsuario(userId));
        userGames.Setup(repository => repository.ObterPorIds(userId, gameId)).ReturnsAsync(userGame);

        var resultado = await controller.Delete(userId, gameId);

        resultado.Should().BeOfType<NoContentResult>();
        userGames.Verify(repository => repository.Remover(userGame), Times.Once);
        userGames.Verify(repository => repository.Salvar(), Times.Once);
    }

    [Fact]
    public async Task Delete_JogoNaoEstaNaBiblioteca_DeveLancarEntidadeNaoEncontrada()
    {
        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var (controller, _, userGames, users) = CriarController(CriarPrincipal(userId));
        users.Setup(repository => repository.ObterPorId(userId)).ReturnsAsync(CriarUsuario(userId));
        userGames.Setup(repository => repository.ObterPorIds(userId, gameId)).ReturnsAsync((UserGame?)null);

        var act = async () => await controller.Delete(userId, gameId);

        await act.Should().ThrowAsync<EntidadeNaoEncontradaException>();
    }

    [Fact]
    public async Task Delete_UsuarioNaoAdminEmBibliotecaAlheia_DeveLancarAcessoNegado()
    {
        var donoId = Guid.NewGuid();
        var invasorId = Guid.NewGuid();
        var (controller, _, _, _) = CriarController(CriarPrincipal(invasorId));

        var act = async () => await controller.Delete(donoId, Guid.NewGuid());

        await act.Should().ThrowAsync<AcessoNegadoException>();
    }
}
```

- [ ] **Step 2: Rodar para ver falhar**

Run: `dotnet test .\FCG.Tests\FCG.Tests.csproj --filter "FullyQualifiedName~BibliotecaControllerTests"`
Expected: RED — falha de compilação se `FCG.Tests.csproj` ainda não referencia `FCG.API` (controller não é visível). Antes de rodar, adicionar ao `FCG.Tests/FCG.Tests.csproj` (ItemGroup de ProjectReference):

```xml
    <ProjectReference Include="..\FCG.API\FCG.API.csproj" />
```

Também falha de comportamento: controller atual não tem `VerificarAcesso`, `DELETE` nem exceção de acesso.

- [ ] **Step 3: Implementar**

Replace the whole content of `FCG.API/Controllers/BibliotecaController.cs` with:

```csharp
using System.Security.Claims;
using FCG.Application.DTOs;
using FCG.Application.Exceptions;
using FCG.Application.Services;
using FCG.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FCG.API.Controllers;

/// <summary>
/// Controlador responsável pela biblioteca de jogos dos usuários.
/// </summary>
[ApiController]
[Route("api/usuarios/{userId:guid}/jogos")]
public class BibliotecaController : ControllerBase
{
    private readonly GameService _gameService;

    /// <summary>
    /// Inicializa uma nova instância do controlador da biblioteca.
    /// </summary>
    public BibliotecaController(GameService gameService)
    {
        _gameService = gameService;
    }

    /// <summary>
    /// Lista os jogos associados à biblioteca de um usuário.
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IEnumerable<GameResponseDTO>>> Get(Guid userId)
    {
        VerificarAcesso(userId);
        var jogos = await _gameService.ObterJogosDoUsuario(userId);
        return Ok(jogos);
    }

    /// <summary>
    /// Adiciona um jogo à biblioteca de um usuário.
    /// </summary>
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Post(Guid userId, [FromBody] AdicionarJogoUsuarioDTO dto)
    {
        VerificarAcesso(userId);
        await _gameService.AdicionarJogoAoUsuario(userId, dto.GameId);
        return Created(string.Empty, null);
    }

    /// <summary>
    /// Remove um jogo da biblioteca de um usuário.
    /// </summary>
    [HttpDelete("{gameId:guid}")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid userId, Guid gameId)
    {
        VerificarAcesso(userId);
        await _gameService.RemoverJogoDoUsuario(userId, gameId);
        return NoContent();
    }

    private void VerificarAcesso(Guid userId)
    {
        if (User.IsInRole(UserRole.Admin.ToString()))
            return;

        var claimId = User.FindFirst("Id")?.Value;
        if (claimId is null || !Guid.TryParse(claimId, out var tokenUserId) || tokenUserId != userId)
            throw new AcessoNegadoException("Você não tem permissão para acessar a biblioteca de outro usuário.");
    }
}
```

- [ ] **Step 4: Rodar para ver passar**

Run: `dotnet test .\FCG.sln`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add FCG.API/Controllers/BibliotecaController.cs FCG.Tests/FCG.Tests.csproj FCG.Tests/API/BibliotecaControllerTests.cs
git commit -m "feat: ownership da biblioteca do usuario e rota DELETE de jogo"
```

---

### Task 5: Seed de Admin configurável no startup

**Files:**
- Create: `FCG.API/Data/AdminSeed.cs`
- Modify: `FCG.API/Program.cs` (chamada do seed após o build)
- Test: `FCG.Tests/API/AdminSeedTests.cs` (novo)

**Interfaces:**
- Consumes: `IUserRepository`, `User`, `UserRole`, `IConfiguration`.
- Produces: `AdminSeed.EnsureCreatedAsync(IUserRepository userRepository, IConfiguration configuration)` (estático, `Task`).

- [ ] **Step 1: Escrever o teste que falha**

Create `FCG.Tests/API/AdminSeedTests.cs`:

```csharp
using FCG.API.Data;
using FCG.Domain.Entities;
using FCG.Domain.Enums;
using FCG.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;

namespace FCG.Tests.API;

public class AdminSeedTests
{
    private static Mock<IConfiguration> CriarConfiguracao(string? email, string? senha)
    {
        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["AdminSeed:Email"]).Returns(email);
        configMock.Setup(c => c["AdminSeed:Senha"]).Returns(senha);
        return configMock;
    }

    [Fact]
    public async Task EnsureCreatedAsync_EmailVazio_DeveDesabilitarSeed()
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        var config = CriarConfiguracao(null, null).Object;

        await AdminSeed.EnsureCreatedAsync(userRepositoryMock.Object, config);

        userRepositoryMock.Verify(repository => repository.Adicionar(It.IsAny<User>()), Times.Never);
        userRepositoryMock.Verify(repository => repository.Salvar(), Times.Never);
    }

    [Fact]
    public async Task EnsureCreatedAsync_UsuarioJaExiste_DeveNaoCriarNovo()
    {
        var adminExistente = new User("Administrador", "admin@fcg.com", "hash", UserRole.Admin);
        var userRepositoryMock = new Mock<IUserRepository>();
        userRepositoryMock.Setup(repository => repository.ObterPorEmail("admin@fcg.com")).ReturnsAsync(adminExistente);
        var config = CriarConfiguracao("admin@fcg.com", "Admin@123").Object;

        await AdminSeed.EnsureCreatedAsync(userRepositoryMock.Object, config);

        userRepositoryMock.Verify(repository => repository.Adicionar(It.IsAny<User>()), Times.Never);
        userRepositoryMock.Verify(repository => repository.Salvar(), Times.Never);
    }

    [Fact]
    public async Task EnsureCreatedAsync_UsuarioAusente_DeveCriarAdminComSenhaHash()
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        userRepositoryMock.Setup(repository => repository.ObterPorEmail("admin@fcg.com")).ReturnsAsync((User?)null);
        var config = CriarConfiguracao("admin@fcg.com", "Admin@123").Object;

        await AdminSeed.EnsureCreatedAsync(userRepositoryMock.Object, config);

        userRepositoryMock.Verify(repository => repository.Adicionar(It.Is<User>(user =>
            user.Email == "admin@fcg.com" &&
            user.Role == UserRole.Admin &&
            BCrypt.Net.BCrypt.Verify("Admin@123", user.SenhaHash))), Times.Once);
        userRepositoryMock.Verify(repository => repository.Salvar(), Times.Once);
    }
}
```

- [ ] **Step 2: Rodar para ver falhar**

Run: `dotnet test .\FCG.Tests\FCG.Tests.csproj --filter "FullyQualifiedName~AdminSeedTests"`
Expected: RED — falha de compilação (`FCG.API.Data.AdminSeed` não existe).

- [ ] **Step 3: Implementar**

Create `FCG.API/Data/AdminSeed.cs`:

```csharp
using FCG.Domain.Entities;
using FCG.Domain.Enums;
using FCG.Domain.Interfaces;
using Microsoft.Extensions.Configuration;

namespace FCG.API.Data;

/// <summary>
/// Cria um usuário administrador inicial de forma idempotente, se configurado.
/// </summary>
public static class AdminSeed
{
    /// <summary>
    /// Garante a existência do usuário admin definido em AdminSeed:Email/AdminSeed:Senha.
    /// Seed desabilitado quando AdminSeed:Email está vazio ou ausente.
    /// </summary>
    public static async Task EnsureCreatedAsync(IUserRepository userRepository, IConfiguration configuration)
    {
        var email = configuration["AdminSeed:Email"];
        if (string.IsNullOrWhiteSpace(email))
            return;

        var usuarioExistente = await userRepository.ObterPorEmail(email);
        if (usuarioExistente is not null)
            return;

        var senha = configuration["AdminSeed:Senha"] ?? string.Empty;
        var senhaHash = BCrypt.Net.BCrypt.HashPassword(senha);
        var admin = new User("Administrador", email, senhaHash, UserRole.Admin);

        await userRepository.Adicionar(admin);
        await userRepository.Salvar();
    }
}
```

Update `FCG.API/Program.cs`:
- Add `using FCG.API.Data;` to the usings.
- After `var app = builder.Build();` and before `// Configure the HTTP request pipeline.`, insert:

```csharp
// Garante a existência do usuário administrador configurado (idempotente).
using (var scope = app.Services.CreateScope())
{
    var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
    await AdminSeed.EnsureCreatedAsync(userRepository, app.Configuration);
}
```

- [ ] **Step 4: Rodar para ver passar**

Run: `dotnet test .\FCG.sln`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add FCG.API/Data/AdminSeed.cs FCG.API/Program.cs FCG.Tests/API/AdminSeedTests.cs
git commit -m "feat: seed de usuario administrador configuravel no startup"
```

---

### Task 6: Validação do ModelState padronizada + controllers enxutos (Auth/Users/Games)

**Files:**
- Modify: `FCG.API/Program.cs`, `FCG.API/Controllers/AuthController.cs`, `FCG.API/Controllers/UsersController.cs`, `FCG.API/Controllers/GamesController.cs`

**Interfaces:**
- Consumes: `ErroResponse` (FCG.API.Middlewares) na factory de ModelState; exceções tipadas das Tasks 1–3.
- Produces: todas as respostas 400 de validação do `[ApiController]` no formato `ErroResponse`; controllers sem try/catch.

- [ ] **Step 1: Escrever o teste que falha (verificação de compilação/funcionamento)**

A factory de `InvalidModelStateResponseFactory` e a remoção de try/catch são configuração/comportamento de pipeline sem cobertura de unit test (sem host nos testes — decisão D9 da spec). A verificação desta task é o build + suíte inteira verde + conferência manual do corpo de erro (item de verificação final). Nenhuma alteração de teste é necessária nesta task; o RED equivalente é o build atual já refletir os controllers com try/catch.

Run (antes das mudanças): `dotnet build .\FCG.sln` — deve compilar (estado atual com try/catch ainda presente).

- [ ] **Step 2: Implementar — factory de ModelState e controllers enxutos**

Update `FCG.API/Program.cs`:
- Add `using Microsoft.AspNetCore.Mvc;` e `using FCG.API.Middlewares;` aos usings.
- Depois de `builder.Services.AddControllers();`, inserir:

```csharp
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var mensagens = context.ModelState.Values
            .SelectMany(value => value.Errors)
            .Select(erro => erro.ErrorMessage)
            .Where(mensagem => !string.IsNullOrWhiteSpace(mensagem))
            .ToList();

        var erroResponse = new ErroResponse
        {
            StatusCode = StatusCodes.Status400BadRequest,
            Mensagem = mensagens.Count == 0 ? "Dados inválidos." : string.Join(" ", mensagens),
            Detalhe = null
        };

        return new BadRequestObjectResult(erroResponse);
    };
});
```

Replace the whole content of `FCG.API/Controllers/AuthController.cs` with:

```csharp
using FCG.Application.DTOs;
using FCG.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace FCG.API.Controllers;

/// <summary>
/// Controlador responsável pela autenticação de usuários.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    /// <summary>
    /// Inicializa uma nova instância do controlador de autenticação.
    /// </summary>
    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Realiza o login do usuário e retorna um token JWT válido.
    /// </summary>
    [HttpPost("login")]
    public async Task<ActionResult<TokenResponseDTO>> Login([FromBody] LoginDTO dto)
    {
        var tokenResponse = await _authService.GerarToken(dto);
        return Ok(tokenResponse);
    }
}
```

Replace the whole content of `FCG.API/Controllers/UsersController.cs` with:

```csharp
using FCG.Application.DTOs;
using FCG.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FCG.API.Controllers;

/// <summary>
/// Controlador responsável pelo gerenciamento de usuários.
/// </summary>
[ApiController]
[Route("api/usuarios")]
public class UsersController : ControllerBase
{
    private readonly UserService _userService;

    /// <summary>
    /// Inicializa uma nova instância do controlador de usuários.
    /// </summary>
    public UsersController(UserService userService)
    {
        _userService = userService;
    }

    /// <summary>
    /// Lista todos os usuários cadastrados.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IEnumerable<UsuarioResponseDTO>>> Get()
    {
        var usuarios = await _userService.ObterTodos();
        return Ok(usuarios);
    }

    /// <summary>
    /// Cria um novo usuário no sistema.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<UsuarioResponseDTO>> Post([FromBody] CriarUsuarioDTO dto)
    {
        var usuario = await _userService.CriarUsuario(dto);
        return Created(string.Empty, usuario);
    }
}
```

Replace the whole content of `FCG.API/Controllers/GamesController.cs` with:

```csharp
using FCG.Application.DTOs;
using FCG.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FCG.API.Controllers;

/// <summary>
/// Controlador responsável pelo catálogo de jogos.
/// </summary>
[ApiController]
[Route("api/jogos")]
public class GamesController : ControllerBase
{
    private readonly GameService _gameService;

    /// <summary>
    /// Inicializa uma nova instância do controlador de jogos.
    /// </summary>
    public GamesController(GameService gameService)
    {
        _gameService = gameService;
    }

    /// <summary>
    /// Lista todos os jogos cadastrados.
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IEnumerable<GameResponseDTO>>> Get()
    {
        var games = await _gameService.ObterTodos();
        return Ok(games);
    }

    /// <summary>
    /// Retorna os dados de um jogo pelo identificador.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<GameResponseDTO>> GetById(Guid id)
    {
        var game = await _gameService.ObterPorId(id);
        return Ok(game);
    }

    /// <summary>
    /// Cria um novo jogo no catálogo.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<GameResponseDTO>> Post([FromBody] CriarGameDTO dto)
    {
        var game = await _gameService.CriarGame(dto);
        return Created(string.Empty, game);
    }

    /// <summary>
    /// Remove um jogo do catálogo pelo identificador.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _gameService.Remover(id);
        return NoContent();
    }
}
```

- [ ] **Step 3: Verificar build e testes**

Run: `dotnet build .\FCG.sln` — deve compilar sem erros. Depois `dotnet test .\FCG.sln` — deve passar.

- [ ] **Step 4: Commit**

```bash
git add FCG.API/Program.cs FCG.API/Controllers/AuthController.cs FCG.API/Controllers/UsersController.cs FCG.API/Controllers/GamesController.cs
git commit -m "feat: validacao de ModelState no formato padronizado e controllers enxutos"
```

---

### Task 7: Configuração (appsettings, launchSettings), limpezas de template e slnx

**Files:**
- Modify: `FCG.API/appsettings.json`, `FCG.API/Properties/launchSettings.json`, `FCG.slnx`
- Delete: `FCG.Application/Class1.cs`, `FCG.Domain/Class1.cs`, `FCG.Infrastructure/Class1.cs`, `FCG.Tests/UnitTest1.cs`

**Interfaces:**
- Produces: config `AdminSeed` em `appsettings.json`; `Jwt:SecretKey` removido do arquivo; `FCG.slnx` com FCG.Tests.

- [ ] **Step 1: Aplicar as mudanças**

Replace the whole content of `FCG.API/appsettings.json` with:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=FCG;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information",
      "FCG": "Information"
    }
  },
  "Jwt": {
    "Issuer": "FCG.API",
    "Audience": "FCG.Client",
    "ExpiracaoHoras": 8
  },
  "AdminSeed": {
    "Email": "gustavo@email.com",
    "Senha": "Gustavo@123"
  },
  "AllowedHosts": "*"
}
```

Note: `Jwt:SecretKey` não está mais no arquivo. Os valores de `AdminSeed` são dev-only (README explica override por `AdminSeed__Email`/`AdminSeed__Senha`).

Update `FCG.API/Properties/launchSettings.json` — em cada um dos 3 profiles (`http`, `https`, `IIS Express`), trocar `"launchUrl": "weatherforecast"` por `"launchUrl": "swagger"`.

Replace the whole content of `FCG.slnx` with:

```xml
<Solution>
  <Project Path="FCG.API/FCG.API.csproj" />
  <Project Path="FCG.Application/FCG.Application.csproj" />
  <Project Path="FCG.Domain/FCG.Domain.csproj" />
  <Project Path="FCG.Infrastructure/FCG.Infrastructure.csproj" />
  <Project Path="FCG.Tests/FCG.Tests.csproj" />
</Solution>
```

Delete os arquivos de resíduo:

```powershell
git rm FCG.Application/Class1.cs FCG.Domain/Class1.cs FCG.Infrastructure/Class1.cs FCG.Tests/UnitTest1.cs
```

- [ ] **Step 2: Verificar build e testes**

Run: `dotnet build .\FCG.slnx && dotnet test .\FCG.sln`
Expected: compila e passa (o `dotnet test` via `FCG.sln` continua válido; `dotnet build .\FCG.slnx` valida o slnx novo).

- [ ] **Step 3: Commit**

```bash
git add FCG.API/appsettings.json FCG.API/Properties/launchSettings.json FCG.slnx
git commit -m "chore: remove residuos de template, ajusta configuracao e inclui testes no slnx"
```

---

### Task 8: Segredo JWT via user-secrets (dev) e documentação

**Files:**
- Modify: `FCG.API/FCG.API.csproj` (via `dotnet user-secrets init` — adiciona `UserSecretsId`), `README.md`, `docs/estado-do-projeto.md`
- Config (fora do repo): user-secrets do projeto `FCG.API`

**Interfaces:**
- Produces: `Jwt:SecretKey` resolvido de user-secrets (Development) ou env `Jwt__SecretKey`; README com instruções de configuração.

- [ ] **Step 1: Inicializar user-secrets e definir o segredo de dev**

Run (na raiz do repo):

```powershell
dotnet user-secrets init --project .\FCG.API\FCG.API.csproj
dotnet user-secrets set "Jwt:SecretKey" "fcg-dev-only-secret-2026-nao-usar-em-producao" --project .\FCG.API\FCG.API.csproj
dotnet user-secrets list --project .\FCG.API\FCG.API.csproj
```

Expected: `user-secrets list` mostra `Jwt:SecretKey = fcg-dev-only-secret-2026-nao-usar-em-producao`. O valor é **dev-only** e fica em `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`, fora do repo.

Verificar que `FCG.API/FCG.API.csproj` ganhou `<UserSecretsId>...</UserSecretsId>` dentro de um `<PropertyGroup>`.

- [ ] **Step 2: Atualizar o README**

Update `README.md`:
1. No trecho "Exemplo da configuração atual" (seção `appsettings.json`), substituir o JSON de exemplo por:

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

2. Na mesma seção, trocar o texto "Ajuste principalmente: `ConnectionStrings:DefaultConnection`, `Jwt:SecretKey`, ..." e adicionar uma subseção "### Chave secreta do JWT" com:

```markdown
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

### Seed do usuário administrador

No primeiro start, se não existir usuário com o e-mail de `AdminSeed:Email`, a aplicação cria um administrador com a senha `AdminSeed:Senha` (dev-only; sobrescreva via `AdminSeed__Email`/`AdminSeed__Senha` em produção).
```

3. Na tabela de endpoints, adicionar a linha:

```markdown
| `DELETE` | `/api/usuarios/{userId}/jogos/{gameId}` | Remove um jogo da biblioteca do usuário | Sim |
```

4. Na seção "Endpoints", acrescentar um parágrafo sobre o formato de erro padronizado:

```markdown
> Todas as respostas de erro usam o formato `{ statusCode, mensagem, detalhe, timestamp }` (`detalhe` apenas em Development). Status: 400 validação, 401 credenciais inválidas, 403 acesso negado, 404 não encontrado, 409 conflito, 500 erro interno.
```

Update `docs/estado-do-projeto.md` — acrescentar ao final da seção 7:

```markdown
**2026-09-09:** direção aprovada; spec em `docs/superpowers/specs/2026-09-09-correcao-monolito-design.md` e plano de implementação em `docs/superpowers/plans/2026-09-09-correcao-monolito.md`. Estado: em implementação.
```

- [ ] **Step 3: Verificar**

Run: `git diff --stat` (mostra alterações esperadas) e `dotnet build .\FCG.sln` (segredo ainda não é necessário para build). Confirme que nenhum arquivo em `appsettings.json` contém o segredo: `Select-String -Path FCG.API\appsettings.json -Pattern "SecretKey"` deve retornar vazio.

- [ ] **Step 4: Commit**

```bash
git add FCG.API/FCG.API.csproj README.md docs/estado-do-projeto.md
git commit -m "chore: segredo JWT via user-secrets em desenvolvimento e documentacao de configuracao"
```

---

### Task 9: Verificação final contra a spec

**Files:** nenhum (verificação) — commits de ajuste se algo falhar.

- [ ] **Step 1: Suíte completa e build das duas solutions**

Run: `dotnet build .\FCG.sln && dotnet test .\FCG.sln`
Expected: build OK; todos os testes passam (contagem total = antigos atualizados + novos de Tasks 1–5).

- [ ] **Step 2: Checklist contra a spec**

Conferir item a item (evidence-based):

- [ ] Item 1 (erros em um padrão): `git grep -l "try" FCG.API/Controllers` retorna vazio; middleware mapeia exceções tipadas (Task 1/6).
- [ ] Item 2 (Admin): `AdminSeed` chamado no `Program.cs` + testes (Task 5).
- [ ] Item 3 (biblioteca): validações 404/409 e ownership (Tasks 3/4).
- [ ] Item 4 (DELETE biblioteca): rota presente e testada (Task 4).
- [ ] Item 5 (Atualizar removido): `git grep -n "Atualizar"` retorna vazio em `FCG.Domain`/`FCG.Infrastructure` (Task 3).
- [ ] Item 6 (login): `AuthService` lança `CredenciaisInvalidasException` com mensagem única (Task 2).
- [ ] Item 7 (stacktrace): middleware só inclui `Detalhe` em Development (Task 1) + teste.
- [ ] Item 8 (resíduos): `Class1.cs`/`UnitTest1.cs` deletados; `launchUrl` = swagger; `FCG.slnx` com Tests (Task 7).
- [ ] Item 9 (segredo): `appsettings.json` sem `SecretKey`; user-secrets configurado (Task 8).

- [ ] **Step 3: Estado do git**

Run: `git status --short` e `git log --oneline -12`
Expected: working tree limpo (apenas eventuais ajustes commitados) e histórico com os commits das Tasks 1–8 em ordem.

- [ ] **Step 4: Commit de ajustes (se houver)**

Se o checklist acusar divergência, corrigir com o menor commit possível e rodar `dotnet test .\FCG.sln` de novo antes de declarar conclusão.
