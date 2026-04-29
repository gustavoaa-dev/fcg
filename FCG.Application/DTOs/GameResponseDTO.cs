namespace FCG.Application.DTOs;

public class GameResponseDTO
{
    public Guid Id { get; set; }
    public string Nome { get; set; }
    public string Descricao { get; set; }
    public decimal Preco { get; set; }
    public DateTime DataCadastro { get; set; }
}
