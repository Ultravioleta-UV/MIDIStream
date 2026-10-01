using Windows.Devices.Enumeration;
using Windows.Devices.Midi;

namespace MidiShortcuts;

internal sealed record MidiEndpoint(string Name, int WinMmIndex, string? WinRtId);

/// <summary>
/// Lista unica de entradas MIDI: las de winmm (USB, virtuales) mas las que
/// solo ve WinRT (Bluetooth LE MIDI emparejado). Si un mismo dispositivo
/// sale por las dos, se queda la de winmm.
/// </summary>
internal static class MidiEndpoints
{
    private static readonly object Sync = new();
    private static List<MidiEndpoint> _cache = new();
    private static DateTime _cachedAt = DateTime.MinValue;

    // La enumeracion WinRT tarda algo; se reutiliza unos segundos.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(3);

    public static string[] GetNames(bool force = false)
        => Get(force).Select(e => e.Name).ToArray();

    public static MidiEndpoint? Find(string name)
    {
        var found = Get(false).FirstOrDefault(e => e.Name == name);
        return found ?? Get(true).FirstOrDefault(e => e.Name == name);
    }

    private static List<MidiEndpoint> Get(bool force)
    {
        lock (Sync)
        {
            if (!force && DateTime.UtcNow - _cachedAt < CacheTtl) return _cache;

            var fresh = Enumerate();
            if (!fresh.Select(e => e.Name).SequenceEqual(_cache.Select(e => e.Name)))
            {
                Logger.Log("Dispositivos MIDI detectados: " +
                           (fresh.Count == 0 ? "(ninguno)" : string.Join(" | ", fresh.Select(e => $"{e.Name} [{(e.WinRtId != null ? "WinRT" : "winmm")}]"))));
            }

            _cache = fresh;
            _cachedAt = DateTime.UtcNow;
            return _cache;
        }
    }

    private static List<MidiEndpoint> Enumerate()
    {
        var list = new List<MidiEndpoint>();

        var winmm = MidiInputDevice.GetDeviceNames();
        for (int i = 0; i < winmm.Length; i++) list.Add(new MidiEndpoint(winmm[i], i, null));

        try
        {
            var winrt = Task.Run(async () =>
            {
                var found = await DeviceInformation.FindAllAsync(MidiInPort.GetDeviceSelector());
                return found.Select(d => (d.Name, d.Id)).ToList();
            }).GetAwaiter().GetResult();

            foreach (var (name, id) in winrt)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;

                bool alreadyInWinMm = winmm.Any(w => !string.IsNullOrWhiteSpace(w) &&
                    (name.StartsWith(w, StringComparison.OrdinalIgnoreCase) ||
                     w.StartsWith(name, StringComparison.OrdinalIgnoreCase)));
                if (alreadyInWinMm) continue;

                // Nombres unicos para poder elegirlos en el combo.
                var unique = name;
                int n = 2;
                while (list.Any(e => e.Name == unique)) unique = $"{name} ({n++})";

                list.Add(new MidiEndpoint(unique, -1, id));
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"No se pudo enumerar dispositivos por WinRT: {ex.Message}");
        }

        return list;
    }
}
