using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RttyDecoder.Audio;
using RttyDecoder.Core;

namespace RttyDecoder.App;

/// <summary>Setarile aplicatiei, in %APPDATA%\YO9VA\RttyDecoder\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Folderul setarilor: %APPDATA%\YO9VA\RttyDecoder.</summary>
    public static readonly string Folder =
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

    /// <summary>Volumul monitorizarii (0-100). Butonul MONITOR porneste mereu oprit.</summary>
    public double MonitorVolume { get; set; } = 50;

    /// <summary>Indicativul propriu (evidentiat altfel, neinclus in lista statiilor).</summary>
    public string? MyCall { get; set; }

    public bool ShowStations { get; set; } = true;

    // Conturile QRZ.com / HamQTH.com pentru verificarea online a indicativelor (ca in CwDecoder).
    // Utilizatorii in clar; parolele criptate cu Windows DPAPI (doar acest cont Windows, pe acest
    // calculator le poate citi). Parolele se citesc/scriu doar prin Get/Set*Password.
    public string? QrzUsername { get; set; }
    public string? QrzPasswordProtected { get; set; }
    public string? HamQthUsername { get; set; }
    public string? HamQthPasswordProtected { get; set; }

    public string GetQrzPassword() => Unprotect(QrzPasswordProtected);
    public void SetQrzPassword(string password) => QrzPasswordProtected = Protect(password);
    public string GetHamQthPassword() => Unprotect(HamQthPasswordProtected);
    public void SetHamQthPassword(string password) => HamQthPasswordProtected = Protect(password);

    private static string? Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return null;
        byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static string Unprotect(string? protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64)) return "";
        try
        {
            byte[] decrypted = ProtectedData.Unprotect(Convert.FromBase64String(protectedBase64), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            // criptat de alt utilizator / alt calculator (settings.json copiat): ca si cum n-ar fi setat
            return "";
        }
    }

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
