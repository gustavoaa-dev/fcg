using FCG.Domain.Entities;

namespace FCG.Domain.Interfaces;

public interface IGameRepository
{
    Task<Game?> ObterPorId(Guid id);
    Task<IEnumerable<Game>> ObterTodos();
    Task Adicionar(Game game);
    Task Atualizar(Game game);
    Task Remover(Game game);
    Task Salvar();
}
