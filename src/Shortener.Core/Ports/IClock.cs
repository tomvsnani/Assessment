namespace Shortener.Core.Ports;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
