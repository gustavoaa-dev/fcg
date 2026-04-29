using FCG.Domain.Entities;

namespace FCG.Domain.Interfaces;

public interface IUserGameRepository
{
    Task<IEnumerable<UserGame>> ObterJogosDoUsuario(Guid userId);
    Task<UserGame?> ObterPorIds(Guid userId, Guid gameId);
    Task Adicionar(UserGame userGame);
    Task Salvar();
}
