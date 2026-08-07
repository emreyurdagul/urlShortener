using LinkService;

namespace LinkService.Tests;

public class CodeGeneratorTests
{
    [Theory]
    [InlineData(1)] // Pro tier
    [InlineData(2)] // Pro tier
    [InlineData(3)] // Plus tier
    [InlineData(4)] // Plus tier
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Generates_requested_length(int length)
    {
        Assert.Equal(length, CodeGenerator.Generate(length).Length);
    }

    [Fact]
    public void Uses_only_base62_alphabet()
    {
        for (var i = 0; i < 100; i++)
            Assert.All(CodeGenerator.Generate(7), c => Assert.Contains(c, CodeGenerator.Alphabet));
    }

    [Fact]
    public void Codes_are_random()
    {
        var codes = Enumerable.Range(0, 100).Select(_ => CodeGenerator.Generate(7)).ToHashSet();
        Assert.True(codes.Count > 95, $"Expected ~100 distinct codes, got {codes.Count}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void Rejects_out_of_range_lengths(int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CodeGenerator.Generate(length));
    }

    [Theory]
    [InlineData("my-link", true)]
    [InlineData("brand_2026", true)]
    [InlineData("a", true)]
    [InlineData("has space", false)]
    [InlineData("api", false)]      // reserved (collides with a literal route)
    [InlineData("health", false)]   // reserved
    [InlineData("", false)]
    [InlineData("way-too-long-vanity-code-exceeding-limit", false)] // > 32 chars
    public void Validates_vanity_codes(string code, bool valid)
    {
        Assert.Equal(valid, CodeGenerator.IsValidVanity(code));
    }
}
