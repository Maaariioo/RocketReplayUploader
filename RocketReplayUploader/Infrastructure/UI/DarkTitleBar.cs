using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RocketReplayUploader.Infrastructure.UI;

public static class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    public static void Apply(Window window, bool dark)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            var value = dark ? 1 : 0;
            _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
        }
        catch
        {
            // Windows anteriores a 1809 no soportan la barra oscura: se ignora.
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
