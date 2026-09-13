namespace Shortener.Core.Links;

/// <summary>
/// The public identifier of a link: 4–16 base62 characters. Value object; invalid values cannot exist.
/// </summary>
public readonly record struct ShortCode
{
    public const int MinLength = 4;
    public const int MaxLength = 16;
    public const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public string Value { get; }

    private ShortCode(string value) => Value = value;

    public static bool TryParse(string? candidate, out ShortCode code)
    {
        code = default;
        if (candidate is null || candidate.Length is < MinLength or > MaxLength)
        {
            return false;
        }

        foreach (var c in candidate)
        {
            if (!Alphabet.Contains(c, StringComparison.Ordinal))
            {
                return false;
            }
        }

        code = new ShortCode(candidate);
        return true;
    }

    public static ShortCode Parse(string candidate) =>
        TryParse(candidate, out var code)
            ? code
            : throw new ArgumentException($"'{candidate}' is not a valid short code.", nameof(candidate));

    public override string ToString() => Value;
}
