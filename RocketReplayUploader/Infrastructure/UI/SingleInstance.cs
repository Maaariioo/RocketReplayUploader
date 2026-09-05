namespace RocketReplayUploader.Infrastructure.UI;

// Permite que solo corra una instancia de la app por usuario. Si el usuario
// lanza el .exe otra vez, se le avisa a la primera instancia (evento "show")
// para que traiga su ventana al frente, y la segunda sale.
public static class SingleInstance
{
    private const string ShowEventFormat = "RocketReplayUploader_Show_{0}";
    private const string MutexFormat = "RocketReplayUploader_{0}";
    private static Mutex? _mutex;

    // Devuelve true si esta instancia es la única (la "principal").
    public static bool TryAcquire(string user)
    {
        _mutex?.Dispose();
        _mutex = new Mutex(initiallyOwned: true, name: string.Format(MutexFormat, user), out var createdNew);
        return createdNew;
    }

    // Pide a la instancia principal que enseñe su ventana.
    public static bool NotifyExisting(string user)
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(string.Format(ShowEventFormat, user));
            ev.Set();
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Crea (o abre) el evento que la instancia principal vigila.
    public static EventWaitHandle? CreateShowSignal(string user)
    {
        try
        {
            return new EventWaitHandle(false, EventResetMode.ManualReset, string.Format(ShowEventFormat, user));
        }
        catch
        {
            return null;
        }
    }

    public static void Release()
    {
        _mutex?.Dispose();
        _mutex = null;
    }
}
