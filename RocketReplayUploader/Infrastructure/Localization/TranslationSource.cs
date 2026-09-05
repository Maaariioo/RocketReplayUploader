using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace RocketReplayUploader.Infrastructure.Localization;

// Fuente única de textos localizados. Desde XAML se enlaza con
//   {Binding [Key], Source={x:Static loc:TranslationSource.Instance}}
// y desde código con Instance["Key"] o Instance.Format("Key", args...).
// Al cambiar de idioma se avisa con PropertyChanged(string.Empty) para que
// todos los bindings del interface se refresquen al instante.
public sealed class TranslationSource : INotifyPropertyChanged
{
    private static readonly Dictionary<string, CultureInfo> Languages = new()
    {
        ["en"] = new CultureInfo("en"),
        ["es"] = new CultureInfo("es"),
        ["fr"] = new CultureInfo("fr")
    };

    public static TranslationSource Instance { get; } = new();

    private readonly ResourceManager _resources = new(
        "RocketReplayUploader.Resources.Strings",
        typeof(TranslationSource).Assembly);

    private string _language = "en";
    private CultureInfo _culture = Languages["en"];

    public string this[string key]
    {
        get
        {
            var value = _resources.GetString(key, _culture);
            return string.IsNullOrEmpty(value) ? key : value;
        }
    }

    public string Format(string key, params object?[] args)
        => string.Format(this[key], args);

    public string Language
    {
        get => _language;
        set
        {
            var lang = Languages.ContainsKey(value) ? value : "en";
            if (_language == lang) return;
            _language = lang;
            _culture = Languages[lang];
            CultureInfo.CurrentUICulture = _culture;
            CultureInfo.CurrentCulture = _culture;
            CultureChanged?.Invoke();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    // Se dispara después de cambiar Language: útil para el código que gestiona
    // textos dinámicos (bandeja, status bars, diálogos).
    public event Action? CultureChanged;

    public event PropertyChangedEventHandler? PropertyChanged;
}