namespace MidiShortcuts;

/// <summary>
/// Decodifica note on/off y control change en claves "note_N" / "cc_N"
/// (sin distinguir canal) y dispara Trigger solo al presionar. Abre el
/// dispositivo por winmm o por WinRT (Bluetooth) segun donde aparezca.
/// </summary>
public sealed class MidiListener : IDisposable
{
    private readonly MidiInputDevice _device = new();
    private WinRtMidiInput? _winrt;
    private readonly HashSet<string> _pressed = new();
    private readonly object _sync = new();
    private volatile bool _learning;

    /// <summary>Se dispara con la clave (ej. "note_60") al presionar un pad/control.</summary>
    public event Action<string>? Trigger;

    /// <summary>Se dispara una sola vez al capturar un pad en modo "aprender".</summary>
    public event Action<string>? Learned;

    /// <summary>Se dispara con cualquier pulsacion, para mostrar "ultimo pad".</summary>
    public event Action<string>? AnyMessage;

    /// <summary>Se dispara si el dispositivo se cierra solo (ej. corte de Bluetooth).</summary>
    public event Action? Disconnected;

    public string? LastError { get; private set; }
    public bool IsOpen => _winrt?.IsOpen ?? _device.IsOpen;

    public MidiListener()
    {
        _device.MessageReceived += OnMessage;
        _device.DeviceClosed += () => Disconnected?.Invoke();
    }

    public bool Start(string portName)
    {
        var endpoint = MidiEndpoints.Find(portName);
        if (endpoint == null)
        {
            LastError = "El dispositivo MIDI ya no esta disponible.";
            return false;
        }

        bool ok;
        if (endpoint.WinRtId != null)
        {
            _winrt = new WinRtMidiInput();
            _winrt.MessageReceived += OnMessage;
            ok = _winrt.Open(endpoint.WinRtId);
        }
        else
        {
            ok = _device.Open(endpoint.WinMmIndex);
        }

        if (!ok) LastError = "No se pudo abrir el puerto MIDI (puede estar en uso por otra instancia).";
        return ok;
    }

    public void StartLearning() => _learning = true;

    private void OnMessage(byte status, byte data1, byte data2)
    {
        int type = status & 0xF0;
        string? key = type switch
        {
            0x90 or 0x80 => $"note_{data1}",
            0xB0 => $"cc_{data1}",
            _ => null
        };
        if (key == null) return;

        bool isPress = type switch
        {
            0x90 => data2 > 0,
            0xB0 => data2 > 0,
            _ => false
        };
        bool isRelease = type switch
        {
            0x90 => data2 == 0,
            0x80 => true,
            0xB0 => data2 == 0,
            _ => false
        };

        if (isPress) AnyMessage?.Invoke(key);

        if (_learning)
        {
            if (isPress)
            {
                _learning = false;
                Logger.Log($"Pad capturado: {key}");
                Learned?.Invoke(key);
            }
            return;
        }

        bool fire = false;
        lock (_sync)
        {
            if (isPress) fire = _pressed.Add(key);
            else if (isRelease) _pressed.Remove(key);
        }

        // Fuera del lock para no bloquear otros mensajes mientras corre la accion.
        if (fire) Trigger?.Invoke(key);
    }

    public void Dispose()
    {
        _device.Dispose();
        _winrt?.Dispose();
    }
}
