using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace MidiShortcuts;

public partial class NewShortcutWindow : Window
{
    private readonly MidiListener _listener;

    public string? CapturedKey { get; private set; }
    public bool Saved { get; private set; }

    public string SelectedType =>
        RbTecla.IsChecked == true ? "tecla" :
        RbTexto.IsChecked == true ? "texto" :
        RbCarpeta.IsChecked == true ? "carpeta" : "app";

    public string SelectedValue => ValueBox.Text ?? "";

    // Constructor sin parametros: solo lo usa el cargador/previsualizador de
    // XAML de Avalonia (evita el warning AVLN3001). La app usa el de abajo.
    public NewShortcutWindow()
    {
        _listener = null!;
        InitializeComponent();
    }

    public NewShortcutWindow(MidiListener listener) : this()
    {
        _listener = listener ?? throw new ArgumentNullException(nameof(listener));

        _listener.Learned += OnLearned;
        Closed += (_, _) => _listener.Learned -= OnLearned;

        CaptureBtn.Click += (_, _) =>
        {
            TargetLbl.Text = "Toca el pad ahora...";
            _listener.StartLearning();
        };

        // El boton "..." abre selector de carpeta o de archivo segun el tipo.
        BrowseBtn.Click += async (_, _) => await BrowseAsync();

        SaveBtn.Click += async (_, _) =>
        {
            if (CapturedKey == null)
            {
                await MessageDialog.ShowAsync(this, "Error", "Primero captura el pad/knob.");
                return;
            }
            if (string.IsNullOrWhiteSpace(ValueBox.Text))
            {
                await MessageDialog.ShowAsync(this, "Error", "Escribe un valor para la accion.");
                return;
            }
            Saved = true;
            Close();
        };
    }

    private async Task BrowseAsync()
    {
        if (RbCarpeta.IsChecked == true)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Selecciona una carpeta",
                AllowMultiple = false,
            });
            if (folders.Count > 0) ValueBox.Text = folders[0].TryGetLocalPath();
        }
        else
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Selecciona un programa o acceso directo",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Programas y accesos directos") { Patterns = new[] { "*.exe", "*.lnk" } },
                    FilePickerFileTypes.All,
                },
            });
            if (files.Count > 0) ValueBox.Text = files[0].TryGetLocalPath();
        }
    }

    private void OnLearned(string key)
    {
        // Llega desde el hilo de winmm.
        Dispatcher.UIThread.Post(() =>
        {
            CapturedKey = key;
            TargetLbl.Text = MappingHelper.DescribeKey(key);
        });
    }
}
