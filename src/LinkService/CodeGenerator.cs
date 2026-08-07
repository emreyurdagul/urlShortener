using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace LinkService;

public static partial class CodeGenerator
{
    // Absolute floor/ceiling for RANDOM codes; per-tier minimums
    // (Plans.MinCodeLength) gate what a given caller may actually request. 1–2
    // char codes are the Pro tier's scarcity perk.
    public const int MinLength = 1;
    public const int MaxLength = 8;

    public const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static string Generate(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, MinLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, MaxLength);
        return RandomNumberGenerator.GetString(Alphabet, length);
    }

    // Vanity (Pro) codes are caller-chosen, so they allow a wider, URL-safe
    // charset than generated codes, but stay bounded. Words that would collide
    // with the service's own literal routes are rejected.
    private static readonly HashSet<string> Reserved =
        new(StringComparer.OrdinalIgnoreCase) { "health", "metrics", "api" };

    public static bool IsValidVanity(string code) =>
        !string.IsNullOrEmpty(code) &&
        code.Length <= 32 &&
        !Reserved.Contains(code) &&
        VanityPattern().IsMatch(code);

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex VanityPattern();
}
