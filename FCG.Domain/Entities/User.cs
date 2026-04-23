using FCG.Domain.Enums;

namespace FCG.Domain.Entities;

public class User
{
    public Guid Id { get; private set; }
    public string Nome { get; private set; }
    public string Email { get; private set; }
    public string SenhaHash { get; private set; }
    public UserRole Role { get; private set; }
    public DateTime DataCadastro { get; private set; }
    public ICollection<UserGame> Jogos { get; private set; }

    public User(string nome, string email, string senhaHash, UserRole role)
    {
        Id = Guid.NewGuid();
        Nome = nome;
        Email = email;
        SenhaHash = senhaHash;
        Role = role;
        DataCadastro = DateTime.UtcNow;
        Jogos = new List<UserGame>();
    }
}
