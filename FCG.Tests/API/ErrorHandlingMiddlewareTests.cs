using System.Text.Json;
using FCG.API.Middlewares;
using FCG.Application.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;

namespace FCG.Tests.API;

public class ErrorHandlingMiddlewareTests
{
    public static IEnumerable<object[]> CasosDeMapeamento()
    {
        yield return new object[] { new ArgumentException("Dados inválidos."), StatusCodes.Status400BadRequest, "Dados inválidos." };
        yield return new object[] { new CredenciaisInvalidasException("Credenciais inválidas."), StatusCodes.Status401Unauthorized, "Credenciais inválidas." };
        yield return new object[] { new UnauthorizedAccessException("Não autorizado."), StatusCodes.Status401Unauthorized, "Não autorizado." };
        yield return new object[] { new AcessoNegadoException("Acesso negado."), StatusCodes.Status403Forbidden, "Acesso negado." };
        yield return new object[] { new EntidadeNaoEncontradaException("Jogo não encontrado."), StatusCodes.Status404NotFound, "Jogo não encontrado." };
        yield return new object[] { new KeyNotFoundException("Chave não encontrada."), StatusCodes.Status404NotFound, "Chave não encontrada." };
        yield return new object[] { new ConflitoException("Já existe."), StatusCodes.Status409Conflict, "Já existe." };
        yield return new object[] { new Exception("boom"), StatusCodes.Status500InternalServerError, "Ocorreu um erro interno no servidor." };
    }

    private static (int StatusCode, string Body) ExecutarComExcecao(Exception excecao, string environment = "Production")
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var loggerMock = new Mock<ILogger<ErrorHandlingMiddleware>>();
        var environmentMock = new Mock<IHostEnvironment>();
        environmentMock.Setup(e => e.EnvironmentName).Returns(environment);

        var middleware = new ErrorHandlingMiddleware(
            _ => { throw excecao; },
            loggerMock.Object,
            environmentMock.Object);

        middleware.InvokeAsync(context).GetAwaiter().GetResult();

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var body = reader.ReadToEnd();

        return (context.Response.StatusCode, body);
    }

    [Theory]
    [MemberData(nameof(CasosDeMapeamento))]
    public void Excecao_DeveRetornarStatusECorpoPadronizado(Exception excecao, int statusEsperado, string mensagemEsperada)
    {
        var (statusCode, body) = ExecutarComExcecao(excecao);

        statusCode.Should().Be(statusEsperado);
        var erro = JsonSerializer.Deserialize<ErroResponse>(body);
        erro!.StatusCode.Should().Be(statusEsperado);
        erro.Mensagem.Should().Be(mensagemEsperada);
        erro.Detalhe.Should().BeNull();
    }

    [Fact]
    public void AmbienteDevelopment_DeveIncluirDetalheComStackTrace()
    {
        var (statusCode, body) = ExecutarComExcecao(new InvalidOperationException("boom"), environment: "Development");

        statusCode.Should().Be(StatusCodes.Status500InternalServerError);
        var erro = JsonSerializer.Deserialize<ErroResponse>(body);
        erro!.Mensagem.Should().Be("Ocorreu um erro interno no servidor.");
        erro.Detalhe.Should().NotBeNullOrEmpty();
    }
}
