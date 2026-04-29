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
    /// <response code="200">Usuários retornados com sucesso.</response>
    /// <response code="400">Falha ao processar a solicitação.</response>
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

    /// <summary>
    /// Cria um novo usuário no sistema.
    /// </summary>
    /// <response code="201">Usuário criado com sucesso.</response>
    /// <response code="400">Dados inválidos para criação do usuário.</response>
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
