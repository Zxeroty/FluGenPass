using System.Security.Cryptography;
using FluGenPass.Models;

namespace FluGenPass.Services;

public sealed class PasswordGeneratorService : IPasswordGeneratorService
{
    private const string UppercaseCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string LowercaseCharacters = "abcdefghijklmnopqrstuvwxyz";
    private const string NumberCharacters = "0123456789";
    private const string SymbolCharacters = "!@#$%^&*()-_=+[]{}<>?/|~";

    public char[] Generate(PasswordOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        int length = Math.Clamp(options.Length, 8, 64);
        char[] password = new char[length];
        
        try
        {
            Generate(options, password);
            return password;
        }
        catch
        {
            password.Clear();
            throw;
        }
    }

    public void Generate(PasswordOptions options, Span<char> destination)
    {
        ArgumentNullException.ThrowIfNull(options);

        string[] selectedGroups = GetSelectedGroups(options).ToArray();

        if (selectedGroups.Length == 0)
        {
            throw new InvalidOperationException("At least one character group must be selected.");
        }

        int length = Math.Clamp(options.Length, 8, 64);
        if (destination.Length < length)
        {
            throw new ArgumentException("Destination span is too short.", nameof(destination));
        }

        string allCharacters = string.Concat(selectedGroups);
        int count = 0;

        foreach (string group in selectedGroups)
        {
            destination[count++] = group[RandomNumberGenerator.GetInt32(group.Length)];
        }

        while (count < length)
        {
            destination[count++] = allCharacters[RandomNumberGenerator.GetInt32(allCharacters.Length)];
        }

        for (int index = length - 1; index > 0; index--)
        {
            int swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (destination[index], destination[swapIndex]) =
                (destination[swapIndex], destination[index]);
        }
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