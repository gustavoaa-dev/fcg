namespace FCG.Application.Exceptions;

public class ConflitoException : Exception
{
    public ConflitoException(string message) : base(message) { }
    public ConflitoException(string message, Exception innerException) : base(message, innerException) { }
}
