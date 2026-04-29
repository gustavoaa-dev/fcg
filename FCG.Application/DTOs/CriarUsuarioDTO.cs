using System.ComponentModel.DataAnnotations;

namespace FCG.Application.DTOs;

public class CriarUsuarioDTO
{
    [Required]
    [MinLength(3)]
    [MaxLength(100)]
    public string Nome { get; set; }

    [Required]
    [EmailAddress]
    public string Email { get; set; }

    [Required]
    [MinLength(8)]
    public string Senha { get; set; }
}
