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
    /// <response code="200">Jogos retornados com sucesso.</response>
    /// <response code="400">Falha ao processar a solicitação.</response>
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IEnumerable<GameResponseDTO>>> Get()
    {
        try
        {
            var games = await _gameService.ObterTodos();
            return Ok(games);
        }
        catch (Exception ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    /// <summary>
    /// Retorna os dados de um jogo pelo identificador.
    /// </summary>
    /// <response code="200">Jogo encontrado com sucesso.</response>
    /// <response code="404">Jogo não encontrado.</response>
    /// <response code="400">Falha ao processar a solicitação.</response>
    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<GameResponseDTO>> GetById(Guid id)
    {
        try
        {
            var game = await _gameService.ObterPorId(id);
            return Ok(game);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { mensagem = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    /// <summary>
    /// Cria um novo jogo no catálogo.
    /// </summary>
    /// <response code="201">Jogo criado com sucesso.</response>
    /// <response code="400">Dados inválidos para criação do jogo.</response>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<GameResponseDTO>> Post([FromBody] CriarGameDTO dto)
    {
        try
        {
            var game = await _gameService.CriarGame(dto);
            return Created(string.Empty, game);
        }
        catch (Exception ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    /// <summary>
    /// Remove um jogo do catálogo pelo identificador.
    /// </summary>
    /// <response code="204">Jogo removido com sucesso.</response>
    /// <response code="404">Jogo não encontrado.</response>
    /// <response code="400">Falha ao processar a solicitação.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            await _gameService.Remover(id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { mensagem = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }
}
