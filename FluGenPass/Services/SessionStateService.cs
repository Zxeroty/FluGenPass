using System.Security.Cryptography;

namespace FluGenPass.Services;

public sealed class SessionStateService : ISessionStateService
{
    private readonly object _keyLock = new();
    private byte[]? _vaultKey;

    public event EventHandler<bool>? UnlockStateChanged;

    public bool IsUnlocked
    {
        get
        {
            lock (_keyLock)
            {
                return _vaultKey is { Length: > 0 };
            }
        }
    }

    public void SetVaultKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (_keyLock)
        {
            if (_vaultKey is not null)
            {
                CryptographicOperations.ZeroMemory(_vaultKey);
            }

            _vaultKey = key.ToArray();
        }

        UnlockStateChanged?.Invoke(this, true);
    }

    public byte[] GetRequiredVaultKey()
    {
        lock (_keyLock)
        {
            if (_vaultKey is null)
            {
                throw new InvalidOperationException("The vault is locked.");
            }

            return _vaultKey.ToArray();
        }
    }

    public void Lock()
    {
        lock (_keyLock)
        {
            if (_vaultKey is not null)
            {
                CryptographicOperations.ZeroMemory(_vaultKey);
                _vaultKey = null;
            }
        }

        UnlockStateChanged?.Invoke(this, false);
    }
}