namespace FCG.Application.Exceptions;

public class EntidadeNaoEncontradaException : Exception
{
    public EntidadeNaoEncontradaException(string message) : base(message) { }
    public EntidadeNaoEncontradaException(string message, Exception innerException) : base(message, innerException) { }
}
