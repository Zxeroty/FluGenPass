using System.Windows;

namespace FluGenPass.Services;

public sealed class ClipboardService : IClipboardService
{
    private System.Timers.Timer? _clearTimer;
    private bool _hasPendingClipboard;
    private string? _lastCopiedText;

    public void SetText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _lastCopiedText = text;
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
                    // Only clear if the clipboard still contains the text we copied
                    if (Clipboard.ContainsText() && Clipboard.GetText() == _lastCopiedText)
                    {
                        Clipboard.Clear();
                    }
                }
                catch
                {
                    // Ignore clipboard access errors
                }
                finally
                {
                    _lastCopiedText = null;
                }
            });
        }
    }
}