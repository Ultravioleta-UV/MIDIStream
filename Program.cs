using System.Runtime.InteropServices;
using Avalonia;

namespace MidiShortcuts;

/// <summary>
/// MIDI Shortcuts - Starrypad mini (version C# / Avalonia)
///
///   MidiShortcuts.exe              -> interfaz grafica
///   MidiShortcuts.exe --minimized  -> arranca directo en la bandeja
///   MidiShortcuts.exe --background -> sin ventana ni icono (modo headless)
/// </summary>
internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [STAThread]
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Logger.Log($"Excepcion no controlada: {ex}");
            ShowError($"La aplicacion encontro un error y debe cerrarse:\n\n{ex?.Message}\n\nRevisa midi_shortcuts.log para mas detalles.");
        };

        try
        {
            bool background = Array.Exists(args, a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));
            if (background)
            {
                return new BackgroundRunner().Run();
            }

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Logger.Log($"Fallo al iniciar: {ex}");
            ShowError($"No se pudo iniciar MIDI Shortcuts:\n\n{ex.Message}\n\nRevisa midi_shortcuts.log para mas detalles.");
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>().UsePlatformDetect();

    private static void ShowError(string text)
        => MessageBoxW(IntPtr.Zero, text, "MIDI Shortcuts - Error", 0x10 /* MB_ICONERROR */);
}
