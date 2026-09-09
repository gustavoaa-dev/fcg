namespace FCG.Application.Exceptions;

public class CredenciaisInvalidasException : Exception
{
    public CredenciaisInvalidasException(string message) : base(message) { }
    public CredenciaisInvalidasException(string message, Exception innerException) : base(message, innerException) { }
}
