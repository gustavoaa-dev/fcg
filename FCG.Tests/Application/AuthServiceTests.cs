using FCG.Application.DTOs;
using FCG.Application.Exceptions;
using FCG.Application.Services;
using FCG.Domain.Entities;
using FCG.Domain.Enums;
using FCG.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace FCG.Tests.Application;

public class AuthServiceTests
{
    [Fact]
    public async Task GerarToken_UsuarioInexistente_DeveLancarCredenciaisInvalidas()
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        userRepositoryMock.Setup(repository => repository.ObterPorEmail("naoexiste@fcg.com"))
            .ReturnsAsync((User?)null);

        var service = new AuthService(userRepositoryMock.Object, Mock.Of<Microsoft.Extensions.Configuration.IConfiguration>());
        var dto = new LoginDTO { Email = "naoexiste@fcg.com", Senha = "Qualquer@1" };

        var act = async () => await service.GerarToken(dto);

        await act.Should().ThrowAsync<CredenciaisInvalidasException>()
            .WithMessage("E-mail ou senha inválidos.");
    }

    [Fact]
    public async Task GerarToken_SenhaIncorreta_DeveLancarCredenciaisInvalidas()
    {
        var senhaHash = BCrypt.Net.BCrypt.HashPassword("SenhaCorreta@1");
        var user = new User("Usuario", "usuario@fcg.com", senhaHash, UserRole.Usuario);

        var userRepositoryMock = new Mock<IUserRepository>();
        userRepositoryMock.Setup(repository => repository.ObterPorEmail("usuario@fcg.com"))
            .ReturnsAsync(user);

        var service = new AuthService(userRepositoryMock.Object, Mock.Of<Microsoft.Extensions.Configuration.IConfiguration>());
        var dto = new LoginDTO { Email = "usuario@fcg.com", Senha = "SenhaErrada@1" };

        var act = async () => await service.GerarToken(dto);

        await act.Should().ThrowAsync<CredenciaisInvalidasException>()
            .WithMessage("E-mail ou senha inválidos.");
    }
}
