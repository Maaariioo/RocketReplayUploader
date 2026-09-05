using System.Windows;

namespace RocketReplayUploader.Infrastructure.UI;

public static class ThemeManager
{
    public static void Apply(string theme)
    {
        if (theme is not ("dark" or "light"))
        {
            theme = "dark";
        }

        var app = System.Windows.Application.Current;
        if (app == null) return;

        try
        {
            // URI absoluta de pack: la relativa no se resuelve al crearla en
            // código y dejaría el diccionario vacío (tema roto en blanco).
            var dict = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Themes/{theme}.xaml")
            };

            if (dict.Count == 0) return;

            // El diccionario de tema siempre es el primero de la cadena.
            if (app.Resources.MergedDictionaries.Count > 0)
            {
                app.Resources.MergedDictionaries.RemoveAt(0);
            }
            app.Resources.MergedDictionaries.Insert(0, dict);
        }
        catch
        {
            // Si algo falla, se conserva el tema ya cargado.
        }
    }
}
