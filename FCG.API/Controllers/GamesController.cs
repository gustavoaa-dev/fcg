using FCG.Application.DTOs;
using FCG.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FCG.API.Controllers;

[ApiController]
[Route("api/jogos")]
public class GamesController : ControllerBase
{
    private readonly GameService _gameService;

    public GamesController(GameService gameService)
    {
        _gameService = gameService;
    }

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
