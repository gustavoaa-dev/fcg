using FCG.API.Data;
using FCG.Domain.Entities;
using FCG.Domain.Enums;
using FCG.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;

namespace FCG.Tests.API;

public class AdminSeedTests
{
    private static Mock<IConfiguration> CriarConfiguracao(string? email, string? senha)
    {
        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["AdminSeed:Email"]).Returns(email);
        configMock.Setup(c => c["AdminSeed:Senha"]).Returns(senha);
        return configMock;
    }

    [Fact]
    public async Task EnsureCreatedAsync_EmailVazio_DeveDesabilitarSeed()
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        var config = CriarConfiguracao(null, null).Object;

        await AdminSeed.EnsureCreatedAsync(userRepositoryMock.Object, config);

        userRepositoryMock.Verify(repository => repository.Adicionar(It.IsAny<User>()), Times.Never);
        userRepositoryMock.Verify(repository => repository.Salvar(), Times.Never);
    }

    [Fact]
    public async Task EnsureCreatedAsync_UsuarioJaExiste_DeveNaoCriarNovo()
    {
        var adminExistente = new User("Administrador", "admin@fcg.com", "hash", UserRole.Admin);
        var userRepositoryMock = new Mock<IUserRepository>();
        userRepositoryMock.Setup(repository => repository.ObterPorEmail("admin@fcg.com")).ReturnsAsync(adminExistente);
        var config = CriarConfiguracao("admin@fcg.com", "Admin@123").Object;

        await AdminSeed.EnsureCreatedAsync(userRepositoryMock.Object, config);

        userRepositoryMock.Verify(repository => repository.Adicionar(It.IsAny<User>()), Times.Never);
        userRepositoryMock.Verify(repository => repository.Salvar(), Times.Never);
    }

    [Fact]
    public async Task EnsureCreatedAsync_UsuarioAusente_DeveCriarAdminComSenhaHash()
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        userRepositoryMock.Setup(repository => repository.ObterPorEmail("admin@fcg.com")).ReturnsAsync((User?)null);
        var config = CriarConfiguracao("admin@fcg.com", "Admin@123").Object;

        User? adminCriado = null;
        userRepositoryMock.Setup(repository => repository.Adicionar(It.IsAny<User>()))
            .Callback<User>(user => adminCriado = user);

        await AdminSeed.EnsureCreatedAsync(userRepositoryMock.Object, config);

        adminCriado.Should().NotBeNull();
        adminCriado!.Email.Should().Be("admin@fcg.com");
        adminCriado.Role.Should().Be(UserRole.Admin);
        BCrypt.Net.BCrypt.Verify("Admin@123", adminCriado.SenhaHash).Should().BeTrue();
        userRepositoryMock.Verify(repository => repository.Salvar(), Times.Once);
    }
}
