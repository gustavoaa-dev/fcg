using FCG.Application.DTOs;
using FCG.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FCG.API.Controllers;

/// <summary>
/// Controlador responsável pelo gerenciamento de usuários.
/// </summary>
[ApiController]
[Route("api/usuarios")]
public class UsersController : ControllerBase
{
    private readonly UserService _userService;

    /// <summary>
    /// Inicializa uma nova instância do controlador de usuários.
    /// </summary>
    public UsersController(UserService userService)
    {
        _userService = userService;
    }

    /// <summary>
    /// Lista todos os usuários cadastrados.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IEnumerable<UsuarioResponseDTO>>> Get()
    {
        var usuarios = await _userService.ObterTodos();
        return Ok(usuarios);
    }

    /// <summary>
    /// Cria um novo usuário no sistema.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<UsuarioResponseDTO>> Post([FromBody] CriarUsuarioDTO dto)
    {
        var usuario = await _userService.CriarUsuario(dto);
        return Created(string.Empty, usuario);
    }
}
