namespace Shortener.Core;

public class LinkNotFoundException : Exception
{
    public LinkNotFoundException(string message) : base(message) { }
}
