using FCG.Domain.Entities;

namespace FCG.Domain.Interfaces;

public interface IUserRepository
{
    Task<User?> ObterPorEmail(string email);
    Task<User?> ObterPorId(Guid id);
    Task<IEnumerable<User>> ObterTodos();
    Task Adicionar(User user);
    Task Salvar();
}
