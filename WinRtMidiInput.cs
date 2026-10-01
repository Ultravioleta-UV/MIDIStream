using Windows.Devices.Midi;
using Windows.Storage.Streams;

namespace MidiShortcuts;

/// <summary>
/// Entrada MIDI por la API WinRT de Windows (Windows.Devices.Midi).
/// Es la que si ve los dispositivos Bluetooth LE MIDI emparejados; la API
/// clasica winmm (la que usa Python/mido) muchas veces NO los lista.
/// </summary>
internal sealed class WinRtMidiInput : IDisposable
{
    private MidiInPort? _port;

    /// <summary>(status, data1, data2), igual que MidiInputDevice.</summary>
    public event Action<byte, byte, byte>? MessageReceived;

    public bool IsOpen { get; private set; }

    public bool Open(string deviceId)
    {
        try
        {
            _port = Task.Run(async () => await MidiInPort.FromIdAsync(deviceId)).GetAwaiter().GetResult();
            if (_port == null) return false;

            _port.MessageReceived += OnMessage;
            IsOpen = true;
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"No se pudo abrir el dispositivo por WinRT: {ex.Message}");
            return false;
        }
    }

    private void OnMessage(MidiInPort sender, MidiMessageReceivedEventArgs args)
    {
        var buffer = args.Message.RawData;
        if (buffer.Length < 1) return;

        var bytes = new byte[buffer.Length];
        DataReader.FromBuffer(buffer).ReadBytes(bytes);

        byte status = bytes[0];
        if (status >= 0xF0) return; // sysex / mensajes de sistema: no interesan

        byte data1 = bytes.Length > 1 ? bytes[1] : (byte)0;
        byte data2 = bytes.Length > 2 ? bytes[2] : (byte)0;
        MessageReceived?.Invoke(status, data1, data2);
    }

    public void Dispose()
    {
        IsOpen = false;
        if (_port != null)
        {
            _port.MessageReceived -= OnMessage;
            _port.Dispose();
            _port = null;
        }
    }
}
