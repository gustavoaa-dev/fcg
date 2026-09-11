using System.Text.Json;
using FCG.Application.Exceptions;

namespace FCG.API.Middlewares;

/// <summary>
/// Middleware responsável por capturar exceções não tratadas e retornar respostas JSON padronizadas.
/// </summary>
public class ErrorHandlingMiddleware
{
    private static readonly System.Text.Json.JsonSerializerOptions OpcoesJson = new(System.Text.Json.JsonSerializerDefaults.Web);
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    /// <summary>
    /// Inicializa uma nova instância do middleware de tratamento global de erros.
    /// </summary>
    public ErrorHandlingMiddleware(
        RequestDelegate next,
        ILogger<ErrorHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    /// <summary>
    /// Processa a requisição HTTP e intercepta exceções para convertê-las em respostas JSON padronizadas.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro não tratado durante o processamento da requisição.");

            var (statusCode, mensagem) = ex switch
            {
                ArgumentException => (StatusCodes.Status400BadRequest, ex.Message),
                CredenciaisInvalidasException or UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, ex.Message),
                AcessoNegadoException => (StatusCodes.Status403Forbidden, ex.Message),
                EntidadeNaoEncontradaException or KeyNotFoundException => (StatusCodes.Status404NotFound, ex.Message),
                ConflitoException => (StatusCodes.Status409Conflict, ex.Message),
                _ => (StatusCodes.Status500InternalServerError, "Ocorreu um erro interno no servidor.")
            };

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = statusCode;

            var erroResponse = new ErroResponse
            {
                StatusCode = statusCode,
                Mensagem = mensagem,
                Detalhe = _environment.IsDevelopment() ? ex.StackTrace : null
            };

            var json = JsonSerializer.Serialize(erroResponse, OpcoesJson);
            await context.Response.WriteAsync(json);
        }
    }
}
