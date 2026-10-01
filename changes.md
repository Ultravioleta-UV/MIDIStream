# MIDI Shortcuts - Starrypad mini (C# / Avalonia)

Misma logica interna que la version WinForms (MIDI por winmm.dll, teclas por
SendInput, config.json, reconexion Bluetooth, modo --background). Solo cambio
la interfaz: ahora es Avalonia.

## Compilar
Requiere .NET 8 SDK e internet la primera vez (restaura los paquetes NuGet de
Avalonia).

    dotnet build -c Release

## Ejecutable portable (un solo .exe)
    dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

## Icono
Reemplaza `MIDI.ico` por el tuyo (mismo nombre). Se usa para el .exe, la
ventana y la bandeja.

## Uso
- `MidiShortcuts.exe`              interfaz grafica
- `MidiShortcuts.exe --minimized`  arranca directo en la bandeja
- `MidiShortcuts.exe --background` modo sin ventana ni icono
- `instalar_inicio.ps1`            lo agrega al inicio de Windows (--minimized)

X = oculta a la bandeja (sigue escuchando). Salir = click derecho en el icono
de la bandeja > Salir.

## Tu config.json
Mismo formato: copia tu `config.json` junto al nuevo .exe y listo.

## Diferencias con la version WinForms
- En la bandeja, un clic izquierdo abre la ventana (Avalonia no expone doble clic).
- No hay globito de aviso al ocultar a la bandeja (Avalonia no lo trae).
