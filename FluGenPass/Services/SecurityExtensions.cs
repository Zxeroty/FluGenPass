using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace FluGenPass.Services;

public static class SecurityExtensions
{
    public static void Clear(this char[]? array)
    {
        if (array == null) return;
        
        // char is 2 bytes in .NET
        Span<byte> byteSpan = MemoryMarshal.Cast<char, byte>(array.AsSpan());
        CryptographicOperations.ZeroMemory(byteSpan);
    }

    public static void Clear(this Span<char> span)
    {
        Span<byte> byteSpan = MemoryMarshal.Cast<char, byte>(span);
        CryptographicOperations.ZeroMemory(byteSpan);
    }
}
