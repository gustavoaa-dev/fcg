using FCG.Application.DTOs;
using FCG.Application.Services;
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
    /// <response code="200">Jogos do usuário retornados com sucesso.</response>
    /// <response code="400">Falha ao processar a solicitação.</response>
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

    /// <summary>
    /// Adiciona um jogo à biblioteca de um usuário.
    /// </summary>
    /// <response code="201">Jogo adicionado à biblioteca com sucesso.</response>
    /// <response code="400">Falha ao adicionar o jogo à biblioteca.</response>
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
