using System.Security.Cryptography;

namespace FluGenPass.Services;

public sealed class SessionStateService : ISessionStateService
{
    private byte[]? _vaultKey;

    public event EventHandler<bool>? UnlockStateChanged;

    public bool IsUnlocked => _vaultKey is { Length: > 0 };

    public void SetVaultKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);

        Lock();

        _vaultKey = key.ToArray();
        UnlockStateChanged?.Invoke(this, true);
    }

    public byte[] GetRequiredVaultKey()
    {
        if (_vaultKey is null)
        {
            throw new InvalidOperationException("The vault is locked.");
        }

        return _vaultKey.ToArray();
    }

    public void Lock()
    {
        if (_vaultKey is not null)
        {
            CryptographicOperations.ZeroMemory(_vaultKey);
            _vaultKey = null;
            UnlockStateChanged?.Invoke(this, false);
        }
    }
}