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
