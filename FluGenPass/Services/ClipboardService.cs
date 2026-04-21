using System.Windows;

namespace FluGenPass.Services;

public sealed class ClipboardService : IClipboardService
{
    private System.Timers.Timer? _clearTimer;
    private bool _hasPendingClipboard;

    public void SetText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Clipboard.SetText(text);
        StartClearTimer();
    }

    private void StartClearTimer()
    {
        _clearTimer?.Stop();
        _clearTimer?.Dispose();
        _hasPendingClipboard = true;

        _clearTimer = new System.Timers.Timer(TimeSpan.FromSeconds(30))
        {
            AutoReset = false,
        };
        _clearTimer.Elapsed += OnClearTimerElapsed;
        _clearTimer.Start();
    }

    private void OnClearTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_hasPendingClipboard)
        {
            _hasPendingClipboard = false;
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        Clipboard.Clear();
                    }
                }
                catch
                {
                    
                }
            });
        }
    }
}