using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using RocketReplayUploader.Infrastructure.Localization;

namespace RocketReplayUploader.Infrastructure.Startup;

public static class StartupInstaller
{
    private const string TaskName = "RocketReplayUploader";
    private const string ServiceName = "RocketReplayUploader";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    // OPCIÓN RECOMENDADA: clave "Run" del registro del usuario actual.
    // Es el mecanismo estándar de Windows para arrancar programas al iniciar
    // sesión: NO requiere permisos de administrador ni contraseñas (schtasks
    // /onlogon da "Acceso denegado" en muchos Windows sin elevación).
    public static void InstallLogonTask(string exePath, bool showConfirmation = true)
    {
        // Arranca minimizado en la bandeja (--minimized) para no molestar al iniciar sesión.
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(TaskName, $"\"{exePath}\" --minimized");

        if (showConfirmation)
        {
            MessageBox.Show(
                TranslationSource.Instance["Inst.Installed"],
                "RocketReplayUploader",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    public static void UninstallLogonTask(bool showConfirmation = true)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(TaskName, throwOnMissingValue: false);

        if (showConfirmation)
        {
            MessageBox.Show(
                TranslationSource.Instance["Inst.Uninstalled"],
                "RocketReplayUploader",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    // OPCIÓN ALTERNATIVA: Servicio de Windows real. Hay que ejecutar esto
    // como Administrador (clic derecho > Ejecutar como administrador).
    // Corre como LocalSystem: si tu carpeta de replays está en una unidad
    // en la nube (OneDrive) con permisos por usuario, puede que no la vea;
    // en ese caso abre services.msc, busca "Rocket Replay Uploader",
    // pestaña "Iniciar sesión como" y pon tu propio usuario y contraseña.
    public static void InstallWindowsService(string exePath)
    {
        Run("sc", $"create {ServiceName} binPath= \"\\\"{exePath}\\\" --service\" start= auto DisplayName= \"Rocket Replay Uploader\"");
        Run("sc", $"description {ServiceName} \"{TranslationSource.Instance["Inst.ServiceDesc"]}\"");
        MessageBox.Show(
            TranslationSource.Instance["Inst.ServiceCreated"],
            "RocketReplayUploader",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    public static void UninstallWindowsService()
    {
        Run("sc", $"stop {ServiceName}");
        Run("sc", $"delete {ServiceName}");
        MessageBox.Show(
            TranslationSource.Instance["Inst.ServiceRemoved"],
            "RocketReplayUploader",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static void Run(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            MessageBox.Show(
                TranslationSource.Instance.Format("Inst.CmdFailed", fileName),
                "RocketReplayUploader",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var details = (stdout + stderr).Trim();
            MessageBox.Show(
                TranslationSource.Instance.Format("Inst.CmdFailedDetails", fileName, arguments, details),
                "RocketReplayUploader",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
