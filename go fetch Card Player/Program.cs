using Avalonia;
using CardPlayer.Services;
using System;

namespace CardPlayer;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (!SingleInstanceService.TryClaimInstance())
        {
            // Another instance is running — signal it to show its editor, then exit
            SingleInstanceService.SignalFirstInstance();
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            SingleInstanceService.Release();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
