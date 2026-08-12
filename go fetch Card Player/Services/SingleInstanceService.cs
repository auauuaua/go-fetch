using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace CardPlayer.Services;

/// <summary>
/// Manages single-instance enforcement via a named mutex and a named pipe.
///
/// First instance  — call StartServer(onActivate) after the app is running.
///                   The server loops waiting for signals from later launches.
///
/// Second instance — call SignalFirstInstance() to wake the running app,
///                   then exit.
/// </summary>
public static class SingleInstanceService
{
    private const string MutexName = "Global\\gofetch_CardPlayer_SingleInstance";
    private const string PipeName  = "gofetch_CardPlayer_Pipe";

    private static Mutex? _mutex;

    /// <summary>
    /// Try to acquire the single-instance mutex.
    /// Returns true if this is the first instance, false if one is already running.
    /// </summary>
    public static bool TryClaimInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, name: MutexName, out bool createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
        }
        return createdNew;
    }

    /// <summary>
    /// Called by the first instance after the UI is ready.
    /// Runs a background loop: each time the pipe receives a connection, invokes <paramref name="onActivate"/>.
    /// </summary>
    public static void StartServer(Action onActivate)
    {
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        transmissionMode: PipeTransmissionMode.Byte,
                        options: PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync();

                    // Read the signal byte (content doesn't matter)
                    _ = server.ReadByte();

                    // Dispatch to UI thread
                    Avalonia.Threading.Dispatcher.UIThread.Post(onActivate);
                }
                catch (Exception)
                {
                    // Pipe broken or app shutting down — brief pause then retry
                    await Task.Delay(500);
                }
            }
        });
    }

    /// <summary>
    /// Called by a second instance to wake the already-running app.
    /// </summary>
    public static void SignalFirstInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".", PipeName, PipeDirection.Out);

            client.Connect(timeout: 2000); // ms
            client.WriteByte(1);
        }
        catch
        {
            // First instance may be starting up or pipe unavailable — ignore
        }
    }

    /// <summary>Release the mutex on clean shutdown.</summary>
    public static void Release()
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        _mutex = null;
    }
}
