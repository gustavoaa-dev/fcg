using FCG.Application.DTOs;
using FCG.Application.Exceptions;
using FCG.Domain.Entities;
using FCG.Domain.Interfaces;

namespace FCG.Application.Services;

public class GameService
{
    private readonly IGameRepository _gameRepository;
    private readonly IUserGameRepository _userGameRepository;
    private readonly IUserRepository _userRepository;

    public GameService(IGameRepository gameRepository, IUserGameRepository userGameRepository, IUserRepository userRepository)
    {
        _gameRepository = gameRepository;
        _userGameRepository = userGameRepository;
        _userRepository = userRepository;
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
            throw new EntidadeNaoEncontradaException("Jogo não encontrado.");

        return MapearParaResponse(game);
    }

    public async Task Remover(Guid id)
    {
        var game = await _gameRepository.ObterPorId(id);
        if (game is null)
            throw new EntidadeNaoEncontradaException("Jogo não encontrado.");

        await _gameRepository.Remover(game);
        await _gameRepository.Salvar();
    }

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

    public async Task<IEnumerable<GameResponseDTO>> ObterJogosDoUsuario(Guid userId)
    {
        var usuario = await _userRepository.ObterPorId(userId);
        if (usuario is null)
            throw new EntidadeNaoEncontradaException("Usuário não encontrado.");

        var userGames = await _userGameRepository.ObterJogosDoUsuario(userId);
        return userGames.Select(userGame => MapearParaResponse(userGame.Game)).ToList();
    }

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
