using System.Runtime.InteropServices;

namespace RocketReplayUploader.Infrastructure.IO;

// Elimina archivos a la Papelera de Windows (con posibilidad de deshacer),
// en lugar de borrarlos de forma irreversible.
public static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public IntPtr pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    private const uint FO_DELETE = 3;
    private const ushort FOF_ALLOWUNDO = 0x40;
    private const ushort FOF_NOCONFIRMATION = 0x10;
    private const ushort FOF_SILENT = 0x4;
    private const ushort FOF_NOERRORUI = 0x400;

    public static bool TryDelete(string path, out string? error)
    {
        error = null;
        try
        {
            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = path + "\0\0",
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
            };

            var result = SHFileOperation(ref op);
            if (result != 0)
            {
                error = $"código {result}";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
}
