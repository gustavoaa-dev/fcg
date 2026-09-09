using FCG.Domain.Entities;
using FCG.Domain.Enums;
using FCG.Domain.Interfaces;
using Microsoft.Extensions.Configuration;

namespace FCG.API.Data;

/// <summary>
/// Cria um usuário administrador inicial de forma idempotente, se configurado.
/// </summary>
public static class AdminSeed
{
    /// <summary>
    /// Garante a existência do usuário admin definido em AdminSeed:Email/AdminSeed:Senha.
    /// Seed desabilitado quando AdminSeed:Email está vazio ou ausente.
    /// </summary>
    public static async Task EnsureCreatedAsync(IUserRepository userRepository, IConfiguration configuration)
    {
        var email = configuration["AdminSeed:Email"];
        if (string.IsNullOrWhiteSpace(email))
            return;

        var senha = configuration["AdminSeed:Senha"];
        if (string.IsNullOrWhiteSpace(senha))
            return;

        var usuarioExistente = await userRepository.ObterPorEmail(email);
        if (usuarioExistente is not null)
            return;

        var senhaHash = BCrypt.Net.BCrypt.HashPassword(senha);
        var admin = new User("Administrador", email, senhaHash, UserRole.Admin);

        await userRepository.Adicionar(admin);
        await userRepository.Salvar();
    }
}
