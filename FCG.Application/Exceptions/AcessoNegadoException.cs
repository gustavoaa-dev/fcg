namespace FCG.Application.Exceptions;

public class AcessoNegadoException : Exception
{
    public AcessoNegadoException(string message) : base(message) { }
    public AcessoNegadoException(string message, Exception innerException) : base(message, innerException) { }
}
