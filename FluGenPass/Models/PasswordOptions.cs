namespace FluGenPass.Models;

public sealed class PasswordOptions
{
    public int Length { get; set; }

    public bool IncludeUppercase { get; set; }

    public bool IncludeLowercase { get; set; }

    public bool IncludeNumbers { get; set; }

    public bool IncludeSymbols { get; set; }
}