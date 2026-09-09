using FCG.Application.DTOs;
using FCG.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace FCG.API.Controllers;

/// <summary>
/// Controlador responsável pela autenticação de usuários.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    /// <summary>
    /// Inicializa uma nova instância do controlador de autenticação.
    /// </summary>
    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Realiza o login do usuário e retorna um token JWT válido.
    /// </summary>
    [HttpPost("login")]
    public async Task<ActionResult<TokenResponseDTO>> Login([FromBody] LoginDTO dto)
    {
        var tokenResponse = await _authService.GerarToken(dto);
        return Ok(tokenResponse);
    }
}
