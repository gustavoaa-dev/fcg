using FCG.Domain.Entities;
using FluentAssertions;

namespace FCG.Tests.Domain;

public class EmailValidationTests
{
    [Fact]
    public void EmailValido_EmailSemArroba_DeveRetornarFalse()
    {
        var email = "usuariodominio.com";

        var resultado = User.EmailValido(email);

        resultado.Should().BeFalse();
    }

    [Fact]
    public void EmailValido_EmailSemDominio_DeveRetornarFalse()
    {
        var email = "usuario@";

        var resultado = User.EmailValido(email);

        resultado.Should().BeFalse();
    }

    [Fact]
    public void EmailValido_EmailSemExtensao_DeveRetornarFalse()
    {
        var email = "usuario@dominio";

        var resultado = User.EmailValido(email);

        resultado.Should().BeFalse();
    }

    [Fact]
    public void EmailValido_EmailVazio_DeveRetornarFalse()
    {
        var email = string.Empty;

        var resultado = User.EmailValido(email);

        resultado.Should().BeFalse();
    }

    [Fact]
    public void EmailValido_EmailValidoFormatoCorreto_DeveRetornarTrue()
    {
        var email = "usuario@dominio.com";

        var resultado = User.EmailValido(email);

        resultado.Should().BeTrue();
    }

    [Fact]
    public void EmailValido_EmailComSubdominioValido_DeveRetornarTrue()
    {
        var email = "usuario@mail.dominio.com";

        var resultado = User.EmailValido(email);

        resultado.Should().BeTrue();
    }
}
