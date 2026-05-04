using FCG.Application.DTOs;
using FCG.Application.Services;
using FCG.Domain.Entities;
using FCG.Domain.Enums;
using FCG.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace FCG.Tests.Application;

public class UserServiceTests
{
    [Fact]
    public async Task CriarUsuario_EmailJaCadastrado_DeveLancarException()
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        var userExistente = new User("Usuario Existente", "existente@fcg.com", "hash", UserRole.Usuario);
        userRepositoryMock.Setup(repository => repository.ObterPorEmail("existente@fcg.com"))
            .ReturnsAsync(userExistente);

        var service = new UserService(userRepositoryMock.Object);
        var dto = new CriarUsuarioDTO
        {
            Nome = "Novo Usuario",
            Email = "existente@fcg.com",
            Senha = "Senha@123"
        };

        var act = async () => await service.CriarUsuario(dto);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CriarUsuario_SenhaInvalida_DeveLancarException()
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        userRepositoryMock.Setup(repository => repository.ObterPorEmail(It.IsAny<string>()))
            .ReturnsAsync((User?)null);

        var service = new UserService(userRepositoryMock.Object);
        var dto = new CriarUsuarioDTO
        {
            Nome = "Usuario Teste",
            Email = "usuario@fcg.com",
            Senha = "123"
        };

        var act = async () => await service.CriarUsuario(dto);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CriarUsuario_DadosValidos_DeveRetornarUsuarioResponseDTO()
    {
        var userRepositoryMock = new Mock<IUserRepository>();
        userRepositoryMock.Setup(repository => repository.ObterPorEmail("usuario@fcg.com"))
            .ReturnsAsync((User?)null);
        userRepositoryMock.Setup(repository => repository.Adicionar(It.IsAny<User>()))
            .Returns(Task.CompletedTask);
        userRepositoryMock.Setup(repository => repository.Salvar())
            .Returns(Task.CompletedTask);

        var service = new UserService(userRepositoryMock.Object);
        var dto = new CriarUsuarioDTO
        {
            Nome = "Usuario Teste",
            Email = "usuario@fcg.com",
            Senha = "Senha@123"
        };

        var resultado = await service.CriarUsuario(dto);

        resultado.Should().NotBeNull();
        resultado.Nome.Should().Be(dto.Nome);
        resultado.Email.Should().Be(dto.Email);
        resultado.Role.Should().Be(UserRole.Usuario.ToString());
        resultado.Id.Should().NotBeEmpty();
        resultado.DataCadastro.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        userRepositoryMock.Verify(repository => repository.Adicionar(It.Is<User>(user =>
            user.Nome == dto.Nome &&
            user.Email == dto.Email &&
            user.Role == UserRole.Usuario &&
            !string.IsNullOrWhiteSpace(user.SenhaHash))), Times.Once);
        userRepositoryMock.Verify(repository => repository.Salvar(), Times.Once);
    }
}
