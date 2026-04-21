using System.Security.Cryptography;
using FluGenPass.Models;

namespace FluGenPass.Services;

public sealed class PasswordGeneratorService : IPasswordGeneratorService
{
    private const string UppercaseCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string LowercaseCharacters = "abcdefghijklmnopqrstuvwxyz";
    private const string NumberCharacters = "0123456789";
    private const string SymbolCharacters = "!@#$%^&*()-_=+[]{}<>?/|~";

    public string Generate(PasswordOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string[] selectedGroups = GetSelectedGroups(options).ToArray();

        if (selectedGroups.Length == 0)
        {
            throw new InvalidOperationException("At least one character group must be selected.");
        }

        int length = Math.Clamp(options.Length, 8, 64);
        List<char> passwordCharacters = new(length);
        string allCharacters = string.Concat(selectedGroups);

        foreach (string group in selectedGroups)
        {
            passwordCharacters.Add(group[RandomNumberGenerator.GetInt32(group.Length)]);
        }

        while (passwordCharacters.Count < length)
        {
            passwordCharacters.Add(allCharacters[RandomNumberGenerator.GetInt32(allCharacters.Length)]);
        }

        for (int index = passwordCharacters.Count - 1; index > 0; index--)
        {
            int swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (passwordCharacters[index], passwordCharacters[swapIndex]) =
                (passwordCharacters[swapIndex], passwordCharacters[index]);
        }

        return new string(passwordCharacters.ToArray());
    }

    public PasswordStrength EvaluateStrength(PasswordOptions options)
    {
        double entropy = EstimateEntropy(options);

        return entropy switch
        {
            < 45 => PasswordStrength.Weak,
            < 70 => PasswordStrength.Medium,
            _ => PasswordStrength.Strong,
        };
    }

    public double EstimateEntropy(PasswordOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        int poolSize = 0;
        int length = Math.Clamp(options.Length, 8, 64);

        if (options.IncludeUppercase)
        {
            poolSize += UppercaseCharacters.Length;
        }

        if (options.IncludeLowercase)
        {
            poolSize += LowercaseCharacters.Length;
        }

        if (options.IncludeNumbers)
        {
            poolSize += NumberCharacters.Length;
        }

        if (options.IncludeSymbols)
        {
            poolSize += SymbolCharacters.Length;
        }

        if (poolSize == 0)
        {
            return 0;
        }

        return length * Math.Log2(poolSize);
    }

    private static IEnumerable<string> GetSelectedGroups(PasswordOptions options)
    {
        if (options.IncludeUppercase)
        {
            yield return UppercaseCharacters;
        }

        if (options.IncludeLowercase)
        {
            yield return LowercaseCharacters;
        }

        if (options.IncludeNumbers)
        {
            yield return NumberCharacters;
        }

        if (options.IncludeSymbols)
        {
            yield return SymbolCharacters;
        }
    }
}