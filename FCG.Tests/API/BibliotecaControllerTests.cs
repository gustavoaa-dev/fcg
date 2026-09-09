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
