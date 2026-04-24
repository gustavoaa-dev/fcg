using FCG.Application.DTOs;
using FCG.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace FCG.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    public async Task<ActionResult<TokenResponseDTO>> Login([FromBody] LoginDTO dto)
    {
        try
        {
            var tokenResponse = await _authService.GerarToken(dto);
            return Ok(tokenResponse);
        }
        catch (Exception ex)
        {
            return Unauthorized(new { mensagem = ex.Message });
        }
    }
}
