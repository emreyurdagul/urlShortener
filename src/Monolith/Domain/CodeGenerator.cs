using System.Security.Cryptography;

namespace Monolith;

public static class CodeGenerator
{
    // 4-character codes are reserved for the premium tier; the public range
    // starts at 5. (Unchanged from the microservice build.)
    public const int MinLength = 5;
    public const int MaxLength = 8;

    public const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static string Generate(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, MinLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, MaxLength);
        return RandomNumberGenerator.GetString(Alphabet, length);
    }
}
