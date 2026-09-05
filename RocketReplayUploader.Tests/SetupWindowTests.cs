using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using RocketReplayUploader.Infrastructure.Config;
using RocketReplayUploader.Views;

namespace RocketReplayUploader.Tests;

// El botón "ojo" de la configuración: alterna entre ocultar y mostrar la API key.
// WPF exige crear las ventanas en un hilo STA, así que el test corre en uno propio.
public class SetupWindowTests
{
    [Fact]
    public void ToggleKey_MuestraYVuelveAOcultarLaClave()
    {
        const string apiKey = "clave-secreta-de-prueba-123";
        var result = "ERROR";
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();

                var window = new SetupWindow(new AppConfig { PlayerName = "Mario", Theme = "dark", BallchasingApiKey = apiKey });

                var txtKey = GetField<PasswordBox>(window, "TxtKey");
                var txtVisible = GetField<TextBox>(window, "TxtKeyVisible");
                var glyph = GetField<TextBlock>(window, "TxtToggleGlyph");
                var handler = typeof(SetupWindow)
                    .GetMethod("BtnToggleKey_Click", BindingFlags.NonPublic | BindingFlags.Instance)!;

                var prefilled = txtKey.Password == apiKey;

                // 1er clic: se muestra la clave en claro (TextBox visible).
                handler.Invoke(window, new object[] { glyph, new RoutedEventArgs() });
                var shown = txtKey.Visibility == Visibility.Collapsed
                            && txtVisible.Visibility == Visibility.Visible
                            && txtVisible.Text == apiKey
                            && glyph.Text == "\uE89F";

                // 2º clic: vuelve a ocultarse, sin perder la clave.
                handler.Invoke(window, new object[] { glyph, new RoutedEventArgs() });
                var hidden = txtKey.Visibility == Visibility.Visible
                             && txtVisible.Visibility == Visibility.Collapsed
                             && txtKey.Password == apiKey
                             && glyph.Text == "\uE890";

                result = $"prefilled={prefilled} shown={shown} hidden={hidden}";
            }
            catch (Exception ex)
            {
                result = "EX: " + ex.GetType().Name + ": " + ex.Message;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));

        Assert.Equal("prefilled=True shown=True hidden=True", result);
    }

    private static T GetField<T>(object target, string name) where T : class =>
        (T)typeof(SetupWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
}
