using FCG.Domain.Entities;
using FCG.Domain.Interfaces;
using FCG.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FCG.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly FCGDbContext _context;

    public UserRepository(FCGDbContext context)
    {
        _context = context;
    }

    public async Task<User?> ObterPorEmail(string email)
    {
        return await _context.Users.FirstOrDefaultAsync(user => user.Email == email);
    }

    public async Task<User?> ObterPorId(Guid id)
    {
        return await _context.Users.FindAsync(id);
    }

    public async Task<IEnumerable<User>> ObterTodos()
    {
        return await _context.Users.ToListAsync();
    }

    public async Task Adicionar(User user)
    {
        await _context.Users.AddAsync(user);
    }

    public async Task Salvar()
    {
        await _context.SaveChangesAsync();
    }
}
