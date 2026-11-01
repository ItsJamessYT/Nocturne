namespace Nocturne.Services;

/// <summary>
/// Ensures only one engine process exists. A second launch asks the running
/// instance to surface its window, then exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Nocturne.SingleInstance";
    private const string EventName = @"Local\Nocturne.ShowWindow";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _listener;

    public bool IsPrimary { get; }
    public event EventHandler? ShowRequested;

    public SingleInstance()
    {
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        IsPrimary = createdNew;
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);

        _listener = new Thread(Listen)
        {
            IsBackground = true,
            Name = "Nocturne instance listener"
        };

        if (IsPrimary)
            _listener.Start();
    }

    public void SignalExistingInstance()
    {
        try
        {
            _showEvent.Set();
        }
        catch (Exception ex)
        {
            Log.Warn("Could not signal running instance: " + ex.Message);
        }
    }

    private void Listen()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                if (_showEvent.WaitOne(500))
                    ShowRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Warn("Instance listener: " + ex.Message);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _showEvent.Set();
        _showEvent.Dispose();
        if (IsPrimary)
        {
            try { _mutex.ReleaseMutex(); } catch { }
        }
        _mutex.Dispose();
    }
}
