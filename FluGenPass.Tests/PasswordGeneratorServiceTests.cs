using FluGenPass.Models;
using FluGenPass.Services;

namespace FluGenPass.Tests;

public sealed class PasswordGeneratorServiceTests
{
    private readonly PasswordGeneratorService _service = new();

    [Fact]
    public void Generate_UsesExactRequestedLength()
    {
        PasswordOptions options = new()
        {
            Length = 32,
            IncludeUppercase = true,
            IncludeLowercase = true,
            IncludeNumbers = true,
            IncludeSymbols = true,
        };

        char[] password = _service.Generate(options);

        Assert.Equal(32, password.Length);
    }

    [Fact]
    public void Generate_IncludesEverySelectedCharacterGroup()
    {
        PasswordOptions options = new()
        {
            Length = 24,
            IncludeUppercase = true,
            IncludeLowercase = true,
            IncludeNumbers = true,
            IncludeSymbols = true,
        };

        char[] password = _service.Generate(options);

        Assert.Contains(password, character => char.IsUpper(character));
        Assert.Contains(password, character => char.IsLower(character));
        Assert.Contains(password, character => char.IsDigit(character));
        Assert.Contains(password, character => !char.IsLetterOrDigit(character));
    }

    [Fact]
    public void Generate_ThrowsWhenAllCharacterGroupsAreDisabled()
    {
        PasswordOptions options = new()
        {
            Length = 16,
            IncludeUppercase = false,
            IncludeLowercase = false,
            IncludeNumbers = false,
            IncludeSymbols = false,
        };

        Assert.Throws<InvalidOperationException>(() => _service.Generate(options));
    }

    [Theory]
    [InlineData(8, false, true, false, false, PasswordStrength.Weak)]
    [InlineData(10, false, true, false, false, PasswordStrength.Medium)]
    [InlineData(12, true, true, true, true, PasswordStrength.Strong)]
    public void EvaluateStrength_UsesExpectedThresholds(
        int length,
        bool uppercase,
        bool lowercase,
        bool numbers,
        bool symbols,
        PasswordStrength expected
    )
    {
        PasswordOptions options = new()
        {
            Length = length,
            IncludeUppercase = uppercase,
            IncludeLowercase = lowercase,
            IncludeNumbers = numbers,
            IncludeSymbols = symbols,
        };

        PasswordStrength strength = _service.EvaluateStrength(options);

        Assert.Equal(expected, strength);
    }
}
