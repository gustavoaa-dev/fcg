using System.Security.Claims;
using FCG.Application.DTOs;
using FCG.Application.Exceptions;
using FCG.Application.Services;
using FCG.Domain.Enums;
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
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IEnumerable<GameResponseDTO>>> Get(Guid userId)
    {
        VerificarAcesso(userId);
        var jogos = await _gameService.ObterJogosDoUsuario(userId);
        return Ok(jogos);
    }

    /// <summary>
    /// Adiciona um jogo à biblioteca de um usuário.
    /// </summary>
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Post(Guid userId, [FromBody] AdicionarJogoUsuarioDTO dto)
    {
        VerificarAcesso(userId);
        await _gameService.AdicionarJogoAoUsuario(userId, dto.GameId);
        return Created(string.Empty, null);
    }

    /// <summary>
    /// Remove um jogo da biblioteca de um usuário.
    /// </summary>
    [HttpDelete("{gameId:guid}")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid userId, Guid gameId)
    {
        VerificarAcesso(userId);
        await _gameService.RemoverJogoDoUsuario(userId, gameId);
        return NoContent();
    }

    private void VerificarAcesso(Guid userId)
    {
        if (User.IsInRole(UserRole.Admin.ToString()))
            return;

        var claimId = User.FindFirst("Id")?.Value;
        if (claimId is null || !Guid.TryParse(claimId, out var tokenUserId) || tokenUserId != userId)
            throw new AcessoNegadoException("Você não tem permissão para acessar a biblioteca de outro usuário.");
    }
}
