using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace MidiShortcuts;

public sealed record ShortcutRow(string Key, string Description, string Type, string Value);

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _watchdog;

    private AppConfig _cfg = AppConfig.Load();
    private MidiListener? _listener;
    private string[] _deviceNames = Array.Empty<string>();
    private string? _listeningPortName;

    // True solo cuando el usuario pulsa "Detener escucha" a proposito;
    // evita que el watchdog reconecte solo si el usuario quiso parar.
    private bool _manualStop;
    private DateTime _nextRetryAt = DateTime.MinValue;
    private DateTime _cfgLoadedAt = DateTime.MinValue;

    // True solo cuando se elige "Salir" desde la bandeja; distingue un
    // cierre real de solo ocultar la ventana a la bandeja al darle a la X.
    private bool _reallyExit;

    public MainWindow()
    {
        InitializeComponent();

        RefreshBtn.Click += (_, _) => RefreshPorts();
        StartBtn.Click += (_, _) => ToggleListen();
        NewBtn.Click += async (_, _) => await OpenNewShortcutDialogAsync();
        DelBtn.Click += (_, _) => DeleteSelected();
        Closing += OnClosing;

        RefreshPorts();
        RefreshTable();

        // Vigila la conexion cada 2 segundos (arranque automatico y
        // reconexion si se corta el Bluetooth). El DispatcherTimer funciona
        // aunque la ventana este oculta en la bandeja.
        _watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _watchdog.Tick += (_, _) => WatchdogTick();
        _watchdog.Start();

        // Primer chequeo casi inmediato, sin esperar el primer intervalo.
        Dispatcher.UIThread.Post(WatchdogTick);
    }

    // ------------------------------------------------------------------
    // Bandeja / cierre
    // ------------------------------------------------------------------

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public void ExitApplication()
    {
        _reallyExit = true;
        _watchdog.Stop();
        StopListening();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_reallyExit && e.CloseReason == WindowCloseReason.WindowClosing)
        {
            // Le dan a la X: se oculta a la bandeja pero sigue escuchando.
            e.Cancel = true;
            Hide();
            return;
        }

        _watchdog.Stop();
        StopListening();
    }

    // ------------------------------------------------------------------
    // Puertos y tabla
    // ------------------------------------------------------------------

    private void SetPorts(string[] names)
    {
        // Solo se toca el combo si la lista cambio de verdad, para no
        // cerrarle el desplegable al usuario mientras elige.
        if (PortCombo.ItemsSource != null && names.SequenceEqual(_deviceNames)) return;

        var selected = PortCombo.SelectedItem as string;
        _deviceNames = names;
        PortCombo.ItemsSource = names;
        if (selected != null && Array.IndexOf(names, selected) >= 0) PortCombo.SelectedItem = selected;
    }

    private void RefreshPorts()
    {
        SetPorts(MidiEndpoints.GetNames(force: true));

        if (PortCombo.SelectedItem is not string sel || Array.IndexOf(_deviceNames, sel) < 0)
        {
            var chosen = DeviceFinder.FindPreferredDevice(_deviceNames, _cfg.PortName);
            if (chosen != null) PortCombo.SelectedItem = chosen;
        }
    }

    private void RefreshTable()
    {
        ShortcutList.ItemsSource = _cfg.Mappings
            .Select(kv => new ShortcutRow(kv.Key, MappingHelper.DescribeKey(kv.Key), kv.Value.Type, kv.Value.Value))
            .ToList();
    }

    private void SetStatus(string text, IBrush brush)
    {
        StatusLbl.Text = text;
        StatusLbl.Foreground = brush;
    }

    // ------------------------------------------------------------------
    // Iniciar / detener / vigilar la conexion
    // ------------------------------------------------------------------

    private void ToggleListen()
    {
        if (_listener == null)
        {
            var portName = PortCombo.SelectedItem as string;
            if (portName == null)
            {
                _ = MessageDialog.ShowAsync(this, "Error", "Selecciona primero un puerto MIDI.");
                return;
            }
            _manualStop = false;
            StartListen(portName, silent: false);
        }
        else
        {
            _manualStop = true;
            StopListening();
            SetStatus("Detenido", Brushes.Red);
            LastPadLbl.Text = "";
            StartBtn.Content = "Iniciar escucha";
        }
    }

    private void StartListen(string portName, bool silent)
    {
        if (Array.IndexOf(_deviceNames, portName) < 0) return;

        // Le pide a una instancia --background (si existe) que libere el puerto.
        BackgroundSignal.RequestStop();

        _cfg.PortName = portName;
        _cfg.Save();

        var listener = new MidiListener();
        listener.Trigger += OnTrigger;
        listener.AnyMessage += OnAnyMessage;
        listener.Disconnected += OnDisconnected;

        if (!listener.Start(portName))
        {
            _nextRetryAt = DateTime.UtcNow.AddSeconds(15);
            listener.Dispose();
            if (silent)
            {
                Logger.Log("No se pudo reconectar automaticamente: puerto ocupado.");
                SetStatus("Reconectando...", Brushes.Orange);
            }
            else
            {
                _ = MessageDialog.ShowAsync(this, "Puerto MIDI ocupado",
                    "No se pudo abrir el puerto MIDI.\n\n" +
                    "Cierra el servicio en segundo plano (MidiShortcuts.exe en el Administrador de tareas) " +
                    "o reinicia el PC, y vuelve a intentarlo.");
                SetStatus("Error de puerto", Brushes.Red);
                StartBtn.Content = "Iniciar escucha";
            }
            return;
        }

        _listener = listener;
        _listeningPortName = portName;
        SetStatus("Escuchando...", Brushes.Green);
        StartBtn.Content = "Detener escucha";
    }

    private void StopListening()
    {
        if (_listener == null) return;
        _listener.Trigger -= OnTrigger;
        _listener.AnyMessage -= OnAnyMessage;
        _listener.Disconnected -= OnDisconnected;
        _listener.Dispose();
        _listener = null;
        _listeningPortName = null;
    }

    private void OnDisconnected()
    {
        // Puede llegar desde el hilo de winmm.
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(OnDisconnected); return; }

        Logger.Log("Dispositivo MIDI cerrado (posible corte de Bluetooth).");
        StopListening();
        if (!_manualStop)
        {
            SetStatus("Reconectando...", Brushes.Orange);
            LastPadLbl.Text = "";
        }
    }

    private void WatchdogTick()
    {
        // Ademas de la notificacion de cierre, se revisa que el puerto
        // siga en la lista, por si Windows no llega a avisar.
        if (_listener != null && _listeningPortName != null)
        {
            var currentNames = MidiEndpoints.GetNames();
            if (Array.IndexOf(currentNames, _listeningPortName) < 0)
            {
                OnDisconnected();
            }
            return;
        }

        if (_manualStop) return;
        if (DateTime.UtcNow < _nextRetryAt) return;

        TryAutoConnect();
    }

    private void TryAutoConnect()
    {
        var names = MidiEndpoints.GetNames();
        if (names.Length == 0)
        {
            SetStatus("Sin dispositivos MIDI", Brushes.Orange);
            return;
        }

        SetPorts(names);

        var chosen = (_cfg.PortName != null && Array.IndexOf(names, _cfg.PortName) >= 0)
            ? _cfg.PortName
            : DeviceFinder.FindPreferredDevice(names, _cfg.PortName);
        if (chosen == null) return;

        PortCombo.SelectedItem = chosen;
        StartListen(chosen, silent: true);
    }

    // ------------------------------------------------------------------
    // Callbacks del listener (llegan desde el hilo de winmm)
    // ------------------------------------------------------------------

    private void OnAnyMessage(string key)
    {
        Dispatcher.UIThread.Post(() => LastPadLbl.Text = $"Ultimo pad: {MappingHelper.DescribeKey(key)}");
    }

    private void OnTrigger(string key)
    {
        // Solo se relee config.json si de verdad cambio, y si algo falla se
        // sigue usando la copia en memoria (asi no se pierden los atajos).
        ReloadConfigIfChanged();
        var action = MappingHelper.FindMapping(_cfg.Mappings, key);
        if (action == null) return;
        try { action.Execute(); }
        catch (Exception ex) { Logger.Log($"Error ejecutando accion: {ex.Message}"); }
    }

    private void ReloadConfigIfChanged()
    {
        try
        {
            if (!File.Exists(AppConfig.ConfigPath)) return;
            var lastWrite = File.GetLastWriteTimeUtc(AppConfig.ConfigPath);
            if (lastWrite <= _cfgLoadedAt) return;

            _cfg = AppConfig.Load();
            _cfgLoadedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Logger.Log($"No se pudo revisar cambios en config.json, se sigue usando la copia en memoria: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // Gestion de atajos
    // ------------------------------------------------------------------

    private void DeleteSelected()
    {
        var rows = ShortcutList.SelectedItems?.OfType<ShortcutRow>().ToList();
        if (rows == null || rows.Count == 0) return;

        foreach (var row in rows) _cfg.Mappings.Remove(row.Key);
        _cfg.Save();
        RefreshTable();
    }

    private async Task OpenNewShortcutDialogAsync()
    {
        if (_listener == null)
        {
            await MessageDialog.ShowAsync(this, "Aviso", "Primero pulsa 'Iniciar escucha' arriba para poder capturar un pad.");
            return;
        }

        var dlg = new NewShortcutWindow(_listener);
        await dlg.ShowDialog(this);

        if (dlg.Saved && dlg.CapturedKey != null)
        {
            _cfg.Mappings[dlg.CapturedKey] = new ShortcutAction { Type = dlg.SelectedType, Value = dlg.SelectedValue };
            _cfg.Save();
            RefreshTable();
        }
    }
}
