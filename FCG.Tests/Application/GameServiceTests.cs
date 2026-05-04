using FCG.Application.Services;
using FCG.Domain.Entities;
using FCG.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace FCG.Tests.Application;

public class GameServiceTests
{
    [Fact]
    public async Task AdicionarJogoAoUsuario_JogoNaoExiste_DeveLancarException()
    {
        var gameRepositoryMock = new Mock<IGameRepository>();
        var userGameRepositoryMock = new Mock<IUserGameRepository>();
        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();

        gameRepositoryMock.Setup(repository => repository.ObterPorId(gameId))
            .ReturnsAsync((Game?)null);

        var service = new GameService(gameRepositoryMock.Object, userGameRepositoryMock.Object);

        var act = async () => await service.AdicionarJogoAoUsuario(userId, gameId);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AdicionarJogoAoUsuario_UsuarioJaPossuiJogo_DeveLancarException()
    {
        var gameRepositoryMock = new Mock<IGameRepository>();
        var userGameRepositoryMock = new Mock<IUserGameRepository>();
        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var game = new Game("Game Teste", "Descricao", 99.90m);
        var userGameExistente = new UserGame(userId, gameId);

        gameRepositoryMock.Setup(repository => repository.ObterPorId(gameId))
            .ReturnsAsync(game);
        userGameRepositoryMock.Setup(repository => repository.ObterPorIds(userId, gameId))
            .ReturnsAsync(userGameExistente);

        var service = new GameService(gameRepositoryMock.Object, userGameRepositoryMock.Object);

        var act = async () => await service.AdicionarJogoAoUsuario(userId, gameId);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AdicionarJogoAoUsuario_DadosValidos_DeveAdicionarNaBiblioteca()
    {
        var gameRepositoryMock = new Mock<IGameRepository>();
        var userGameRepositoryMock = new Mock<IUserGameRepository>();
        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var game = new Game("Game Teste", "Descricao", 99.90m);

        gameRepositoryMock.Setup(repository => repository.ObterPorId(gameId))
            .ReturnsAsync(game);
        userGameRepositoryMock.Setup(repository => repository.ObterPorIds(userId, gameId))
            .ReturnsAsync((UserGame?)null);
        userGameRepositoryMock.Setup(repository => repository.Adicionar(It.IsAny<UserGame>()))
            .Returns(Task.CompletedTask);
        userGameRepositoryMock.Setup(repository => repository.Salvar())
            .Returns(Task.CompletedTask);

        var service = new GameService(gameRepositoryMock.Object, userGameRepositoryMock.Object);

        await service.AdicionarJogoAoUsuario(userId, gameId);

        userGameRepositoryMock.Verify(repository => repository.Adicionar(It.Is<UserGame>(userGame =>
            userGame.UserId == userId &&
            userGame.GameId == gameId)), Times.Once);
        userGameRepositoryMock.Verify(repository => repository.Salvar(), Times.Once);
    }
}
