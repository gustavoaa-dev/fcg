namespace FCG.API.Middlewares;

/// <summary>
/// Representa o formato padronizado de resposta para erros da API.
/// </summary>
public class ErroResponse
{
    /// <summary>
    /// Código HTTP retornado pela API.
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// Mensagem principal do erro.
    /// </summary>
    public string Mensagem { get; set; } = string.Empty;

    /// <summary>
    /// Detalhe adicional do erro quando disponível.
    /// </summary>
    public string? Detalhe { get; set; }

    /// <summary>
    /// Data e hora UTC em que o erro foi gerado.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
