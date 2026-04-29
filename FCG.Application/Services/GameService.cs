using FCG.Application.DTOs;
using FCG.Domain.Entities;
using FCG.Domain.Interfaces;

namespace FCG.Application.Services;

public class GameService
{
    private readonly IGameRepository _gameRepository;
    private readonly IUserGameRepository _userGameRepository;

    public GameService(IGameRepository gameRepository, IUserGameRepository userGameRepository)
    {
        _gameRepository = gameRepository;
        _userGameRepository = userGameRepository;
    }

    public async Task<GameResponseDTO> CriarGame(CriarGameDTO dto)
    {
        if (dto is null)
            throw new ArgumentNullException(nameof(dto), "Os dados do jogo são obrigatórios.");

        var game = new Game(dto.Nome, dto.Descricao, dto.Preco);

        await _gameRepository.Adicionar(game);
        await _gameRepository.Salvar();

        return MapearParaResponse(game);
    }

    public async Task<IEnumerable<GameResponseDTO>> ObterTodos()
    {
        var games = await _gameRepository.ObterTodos();
        return games.Select(MapearParaResponse).ToList();
    }

    public async Task<GameResponseDTO> ObterPorId(Guid id)
    {
        var game = await _gameRepository.ObterPorId(id);
        if (game is null)
            throw new InvalidOperationException("Jogo não encontrado.");

        return MapearParaResponse(game);
    }

    public async Task Remover(Guid id)
    {
        var game = await _gameRepository.ObterPorId(id);
        if (game is null)
            throw new InvalidOperationException("Jogo não encontrado.");

        await _gameRepository.Remover(game);
        await _gameRepository.Salvar();
    }

    public async Task AdicionarJogoAoUsuario(Guid userId, Guid gameId)
    {
        var game = await _gameRepository.ObterPorId(gameId);
        if (game is null)
            throw new InvalidOperationException("Jogo não encontrado.");

        var userGameExistente = await _userGameRepository.ObterPorIds(userId, gameId);
        if (userGameExistente is not null)
            throw new InvalidOperationException("O usuário já possui este jogo.");

        var userGame = new UserGame(userId, gameId);

        await _userGameRepository.Adicionar(userGame);
        await _userGameRepository.Salvar();
    }

    public async Task<IEnumerable<GameResponseDTO>> ObterJogosDoUsuario(Guid userId)
    {
        var userGames = await _userGameRepository.ObterJogosDoUsuario(userId);
        return userGames.Select(userGame => MapearParaResponse(userGame.Game)).ToList();
    }

    private static GameResponseDTO MapearParaResponse(Game game)
    {
        return new GameResponseDTO
        {
            Id = game.Id,
            Nome = game.Nome,
            Descricao = game.Descricao,
            Preco = game.Preco,
            DataCadastro = game.DataCadastro
        };
    }
}
