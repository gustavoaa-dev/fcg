using FCG.Domain.Entities;
using FCG.Domain.Interfaces;
using FCG.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FCG.Infrastructure.Repositories;

public class GameRepository : IGameRepository
{
    private readonly FCGDbContext _context;

    public GameRepository(FCGDbContext context)
    {
        _context = context;
    }

    public async Task<Game?> ObterPorId(Guid id)
    {
        return await _context.Games.FindAsync(id);
    }

    public async Task<IEnumerable<Game>> ObterTodos()
    {
        return await _context.Games.ToListAsync();
    }

    public async Task Adicionar(Game game)
    {
        await _context.Games.AddAsync(game);
    }

    public Task Atualizar(Game game)
    {
        _context.Games.Update(game);
        return Task.CompletedTask;
    }

    public Task Remover(Game game)
    {
        _context.Games.Remove(game);
        return Task.CompletedTask;
    }

    public async Task Salvar()
    {
        await _context.SaveChangesAsync();
    }
}
