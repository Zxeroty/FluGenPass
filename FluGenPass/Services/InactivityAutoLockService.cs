using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace FluGenPass.Services;

public sealed class InactivityAutoLockService : IInactivityAutoLockService
{
    private readonly ISessionStateService _sessionState;
    private readonly DispatcherTimer _timer;
    private TimeSpan _timeout;

    public InactivityAutoLockService(
        ISessionStateService sessionState,
        TimeSpan timeout
    )
    {
        _sessionState = sessionState;
        _timeout = timeout;

        _timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = _timeout,
        };
        _timer.Tick += OnTimerTick;

        _sessionState.UnlockStateChanged += OnUnlockStateChanged;

        InputManager.Current.PreProcessInput += OnProcessInput;
    }

    public bool IsEnabled { get; set; }

    public TimeSpan Timeout
    {
        get => _timeout;
        set
        {
            _timeout = value;
            _timer.Interval = _timeout;
        }
    }

    public void ResetTimer()
    {
        if (!IsEnabled || !_sessionState.IsUnlocked)
        {
            return;
        }

        _timer.Stop();
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        InputManager.Current.PreProcessInput -= OnProcessInput;
        _sessionState.UnlockStateChanged -= OnUnlockStateChanged;
    }

    private void OnProcessInput(object? sender, ProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is KeyboardEventArgs or MouseEventArgs)
        {
            ResetTimer();
        }
    }

    private void OnUnlockStateChanged(object? sender, bool isUnlocked)
    {
        if (isUnlocked)
        {
            ResetTimer();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        _sessionState.Lock();
    }
}