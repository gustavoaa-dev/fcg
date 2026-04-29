using FCG.Domain.Entities;
using FCG.Domain.Interfaces;
using FCG.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FCG.Infrastructure.Repositories;

public class UserGameRepository : IUserGameRepository
{
    private readonly FCGDbContext _context;

    public UserGameRepository(FCGDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<UserGame>> ObterJogosDoUsuario(Guid userId)
    {
        return await _context.UserGames
            .Include(userGame => userGame.Game)
            .Where(userGame => userGame.UserId == userId)
            .ToListAsync();
    }

    public async Task<UserGame?> ObterPorIds(Guid userId, Guid gameId)
    {
        return await _context.UserGames
            .FirstOrDefaultAsync(userGame => userGame.UserId == userId && userGame.GameId == gameId);
    }

    public async Task Adicionar(UserGame userGame)
    {
        await _context.UserGames.AddAsync(userGame);
    }

    public async Task Salvar()
    {
        await _context.SaveChangesAsync();
    }
}
