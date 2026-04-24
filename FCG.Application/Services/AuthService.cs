using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FCG.Application.DTOs;
using FCG.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace FCG.Application.Services;

public class AuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IConfiguration _configuration;

    public AuthService(IUserRepository userRepository, IConfiguration configuration)
    {
        _userRepository = userRepository;
        _configuration = configuration;
    }

    public async Task<TokenResponseDTO> GerarToken(LoginDTO dto)
    {
        if (dto is null)
            throw new ArgumentNullException(nameof(dto), "Os dados de login são obrigatórios.");

        var user = await _userRepository.ObterPorEmail(dto.Email);
        if (user is null)
            throw new InvalidOperationException("Usuário não encontrado.");

        var senhaValida = BCrypt.Net.BCrypt.Verify(dto.Senha, user.SenhaHash);
        if (!senhaValida)
            throw new UnauthorizedAccessException("Senha inválida.");

        var secretKey = _configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException("A configuração Jwt:SecretKey não foi encontrada.");
        var issuer = _configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("A configuração Jwt:Issuer não foi encontrada.");
        var audience = _configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("A configuração Jwt:Audience não foi encontrada.");

        if (!int.TryParse(_configuration["Jwt:ExpiracaoHoras"], out var expiracaoHoras))
            throw new InvalidOperationException("A configuração Jwt:ExpiracaoHoras é inválida.");

        var expiracao = DateTime.UtcNow.AddHours(expiracaoHoras);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new("Id", user.Id.ToString()),
            new("Email", user.Email),
            new("Nome", user.Nome),
            new("Role", user.Role.ToString())
        };

        var tokenDescriptor = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiracao,
            signingCredentials: credentials);

        var token = new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);

        return new TokenResponseDTO
        {
            Token = token,
            Expiracao = expiracao
        };
    }
}
