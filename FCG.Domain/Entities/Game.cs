namespace FCG.Domain.Entities;

public class Game
{
    public Guid Id { get; private set; }
    public string Nome { get; private set; }
    public string Descricao { get; private set; }
    public decimal Preco { get; private set; }
    public DateTime DataCadastro { get; private set; }
    public ICollection<UserGame> Usuarios { get; private set; }

    public Game(string nome, string descricao, decimal preco)
    {
        Id = Guid.NewGuid();
        Nome = nome;
        Descricao = descricao;
        Preco = preco;
        DataCadastro = DateTime.UtcNow;
        Usuarios = new List<UserGame>();
    }
}
