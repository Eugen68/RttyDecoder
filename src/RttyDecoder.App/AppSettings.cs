using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RttyDecoder.Audio;
using RttyDecoder.Core;

namespace RttyDecoder.App;

/// <summary>Setarile aplicatiei, in %APPDATA%\YO9VA\RttyDecoder\settings.json.</summary>
public sealed class AppSettings
{
    private static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YO9VA", "RttyDecoder");

    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public RttySettings? Rtty { get; set; }

    public string? DeviceId { get; set; }

    public AudioChannel Channel { get; set; } = AudioChannel.Left;

    public bool RealTime { get; set; } = true;

    public double Width { get; set; }

    public double Height { get; set; }

    public bool Maximized { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
        }
        catch
        {
            // fisier corupt: pornim cu valorile implicite
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch
        {
            // nu blocam inchiderea aplicatiei
        }
    }
}
