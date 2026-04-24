using FCG.Domain.Enums;
using System.Text.RegularExpressions;

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

    public static bool EmailValido(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
    }

    public static bool SenhaValida(string senha)
    {
        if (string.IsNullOrWhiteSpace(senha))
            return false;

        return Regex.IsMatch(senha, @"^(?=.*[A-Za-z])(?=.*\d)(?=.*[^A-Za-z\d]).{8,}$");
    }
}
