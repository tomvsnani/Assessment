namespace Shortener.Core;

public class InvalidUrlException : Exception
{
    public InvalidUrlException(string message) : base(message) { }
}
