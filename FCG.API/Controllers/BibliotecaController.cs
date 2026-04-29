using FCG.Application.DTOs;
using FCG.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FCG.API.Controllers;

[ApiController]
[Route("api/usuarios/{userId:guid}/jogos")]
public class BibliotecaController : ControllerBase
{
    private readonly GameService _gameService;

    public BibliotecaController(GameService gameService)
    {
        _gameService = gameService;
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IEnumerable<GameResponseDTO>>> Get(Guid userId)
    {
        try
        {
            var jogos = await _gameService.ObterJogosDoUsuario(userId);
            return Ok(jogos);
        }
        catch (Exception ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Post(Guid userId, [FromBody] AdicionarJogoUsuarioDTO dto)
    {
        try
        {
            await _gameService.AdicionarJogoAoUsuario(userId, dto.GameId);
            return Created(string.Empty, null);
        }
        catch (Exception ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }
}
