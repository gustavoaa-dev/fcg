namespace FCG.Application.DTOs;

public class UsuarioResponseDTO
{
    public Guid Id { get; set; }
    public string Nome { get; set; }
    public string Email { get; set; }
    public string Role { get; set; }
    public DateTime DataCadastro { get; set; }
}
