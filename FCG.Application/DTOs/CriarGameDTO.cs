using System.ComponentModel.DataAnnotations;

namespace FCG.Application.DTOs;

public class CriarGameDTO
{
    [Required]
    [MinLength(3)]
    [MaxLength(100)]
    public string Nome { get; set; }

    [Required]
    [MaxLength(500)]
    public string Descricao { get; set; }

    [Required]
    [Range(0.01, 9999.99)]
    public decimal Preco { get; set; }
}
