namespace FCG.Domain.Entities;

public class UserGame
{
    public Guid UserId { get; private set; }
    public Guid GameId { get; private set; }
    public DateTime DataCompra { get; private set; }
    public User User { get; private set; } = null!;
    public Game Game { get; private set; } = null!;

    private UserGame()
    {
    }

    public UserGame(Guid userId, Guid gameId)
    {
        UserId = userId;
        GameId = gameId;
        DataCompra = DateTime.UtcNow;
    }
}
