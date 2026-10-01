using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace MidiShortcuts;

public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindow? _mainWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;

            // La app NO se cierra al cerrar la ventana (X): se oculta a la
            // bandeja. Solo termina con "Salir" en el menu de la bandeja.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            bool minimized = desktop.Args?.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)) == true;

            // La ventana se crea siempre (arranca la escucha), pero solo se
            // muestra si no se pidio --minimized.
            _mainWindow = new MainWindow();
            if (!minimized) _mainWindow.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnTrayClicked(object? sender, EventArgs e) => _mainWindow?.ShowFromTray();

    private void OnOpenClick(object? sender, EventArgs e) => _mainWindow?.ShowFromTray();

    private void OnExitClick(object? sender, EventArgs e)
    {
        _mainWindow?.ExitApplication();

        // Quita el icono de la bandeja antes de salir para que no quede "fantasma".
        var icons = TrayIcon.GetIcons(this);
        if (icons != null)
        {
            foreach (var icon in icons) icon.Dispose();
        }

        _desktop?.Shutdown();
    }
}
