using Shortener.Core.Links;

namespace Shortener.Core.Ports;

public interface IShortCodeGenerator
{
    ShortCode Generate(int length);
}
