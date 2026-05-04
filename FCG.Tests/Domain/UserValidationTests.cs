using FCG.Domain.Entities;
using FluentAssertions;

namespace FCG.Tests.Domain;

public class UserValidationTests
{
    [Fact]
    public void SenhaValida_SenhaComMenosDe8Caracteres_DeveRetornarFalse()
    {
        var senha = "Ab1@xyz";

        var resultado = User.SenhaValida(senha);

        resultado.Should().BeFalse();
    }

    [Fact]
    public void SenhaValida_SenhaSemNumeros_DeveRetornarFalse()
    {
        var senha = "Abcdef@#";

        var resultado = User.SenhaValida(senha);

        resultado.Should().BeFalse();
    }

    [Fact]
    public void SenhaValida_SenhaSemLetras_DeveRetornarFalse()
    {
        var senha = "1234567@";

        var resultado = User.SenhaValida(senha);

        resultado.Should().BeFalse();
    }

    [Fact]
    public void SenhaValida_SenhaSemCaracteresEspeciais_DeveRetornarFalse()
    {
        var senha = "Abcdef12";

        var resultado = User.SenhaValida(senha);

        resultado.Should().BeFalse();
    }

    [Fact]
    public void SenhaValida_SenhaVazia_DeveRetornarFalse()
    {
        var senha = string.Empty;

        var resultado = User.SenhaValida(senha);

        resultado.Should().BeFalse();
    }

    [Fact]
    public void SenhaValida_SenhaValidaComTodosOsRequisitos_DeveRetornarTrue()
    {
        var senha = "Senha@123";

        var resultado = User.SenhaValida(senha);

        resultado.Should().BeTrue();
    }

    [Fact]
    public void SenhaValida_SenhaComExatamente8CaracteresValidos_DeveRetornarTrue()
    {
        var senha = "Ab1@cdef";

        var resultado = User.SenhaValida(senha);

        resultado.Should().BeTrue();
    }
}
