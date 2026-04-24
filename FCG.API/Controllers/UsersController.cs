using FCG.Application.DTOs;
using FCG.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FCG.API.Controllers;

[ApiController]
[Route("api/usuarios")]
public class UsersController : ControllerBase
{
    private readonly UserService _userService;

    public UsersController(UserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IEnumerable<UsuarioResponseDTO>>> Get()
    {
        try
        {
            var usuarios = await _userService.ObterTodos();
            return Ok(usuarios);
        }
        catch (Exception ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }

    [HttpPost]
    public async Task<ActionResult<UsuarioResponseDTO>> Post([FromBody] CriarUsuarioDTO dto)
    {
        try
        {
            var usuario = await _userService.CriarUsuario(dto);
            return Created(string.Empty, usuario);
        }
        catch (Exception ex)
        {
            return BadRequest(new { mensagem = ex.Message });
        }
    }
}
