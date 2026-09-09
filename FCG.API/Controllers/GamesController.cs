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
