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
        
        try
        {
            var dataObject = new DataObject();
            dataObject.SetText(text);

            // Prevent Windows Clipboard History (Win + V)
            var preventHistoryStream = new System.IO.MemoryStream(new byte[] { 0, 0, 0, 0 });
            dataObject.SetData("ExcludeClipboardClipFromMonitorProcessing", preventHistoryStream);

            // Prevent Cloud Syncing of Clipboard data
            var preventCloudStream = new System.IO.MemoryStream(new byte[] { 0, 0, 0, 0 });
            dataObject.SetData("CanUploadToCloudClipboard", preventCloudStream);

            // Prevent Bookmarking / Pinning of Clipboard data
            var preventPinStream = new System.IO.MemoryStream(new byte[] { 0, 0, 0, 0 });
            dataObject.SetData("CanBookMarkClipboardClip", preventPinStream);

            Clipboard.SetDataObject(dataObject, copy: true);
        }
        catch
        {
            // Fallback to standard clipboard in case setting complex DataObject fails
            Clipboard.SetText(text);
        }

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