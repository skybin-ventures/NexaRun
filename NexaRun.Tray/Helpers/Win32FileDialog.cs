using System.Runtime.InteropServices;
using System.Text;

namespace NexaRun.Tray.Helpers;

/// <summary>
/// Native open/save dialogs for tray actions when no Avalonia window is visible.
/// Avalonia StorageProvider needs a real HWND; a 1×1 host often never shows a picker.
/// </summary>
internal static class Win32FileDialog
{
    private const int OfnExplorer = 0x00080000;
    private const int OfnFileMustExist = 0x00001000;
    private const int OfnPathMustExist = 0x00000800;
    private const int OfnOverwritePrompt = 0x00000002;
    private const int OfnHidereadonly = 0x00000004;

    public static string? Open(string title, string? initialDirectory, params string[] patterns)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var filter = BuildFilter(patterns);
        var fileBuffer = new StringBuilder(1024);
        var ofn = new OpenFileName
        {
            lStructSize = Marshal.SizeOf<OpenFileName>(),
            lpstrFilter = filter,
            lpstrFile = fileBuffer,
            nMaxFile = fileBuffer.Capacity,
            lpstrTitle = title,
            lpstrInitialDir = DirectoryOrNull(initialDirectory),
            Flags = OfnExplorer | OfnFileMustExist | OfnPathMustExist | OfnHidereadonly
        };

        return GetOpenFileName(ref ofn) ? NullIfBlank(ofn.lpstrFile?.ToString()) : null;
    }

    public static string? Save(string title, string defaultName, string? initialDirectory)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var fileBuffer = new StringBuilder(1024);
        if (!string.IsNullOrWhiteSpace(defaultName))
            fileBuffer.Append(defaultName);

        var ofn = new OpenFileName
        {
            lStructSize = Marshal.SizeOf<OpenFileName>(),
            lpstrFilter = "NexaRun export (*.json)\0*.json\0All files (*.*)\0*.*\0\0",
            lpstrFile = fileBuffer,
            nMaxFile = fileBuffer.Capacity,
            lpstrTitle = title,
            lpstrDefExt = "json",
            lpstrInitialDir = DirectoryOrNull(initialDirectory),
            Flags = OfnExplorer | OfnPathMustExist | OfnOverwritePrompt | OfnHidereadonly
        };

        if (!GetSaveFileName(ref ofn))
            return null;

        var path = NullIfBlank(ofn.lpstrFile?.ToString());
        if (path == null)
            return null;

        return path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? path : path + ".json";
    }

    /// <summary>Convert Avalonia-style "Label|*.ext;*.ext2" entries to Win32 filter.</summary>
    private static string BuildFilter(string[] patterns)
    {
        if (patterns.Length == 0)
            return "All files (*.*)\0*.*\0\0";

        var sb = new StringBuilder();
        foreach (var pattern in patterns)
        {
            var parts = pattern.Split('|', 2);
            var label = parts[0];
            var glob = parts.Length > 1 ? parts[1] : "*.*";
            var display = glob.Contains(';', StringComparison.Ordinal)
                ? $"{label} ({glob.Replace(';', ',')})"
                : $"{label} ({glob})";
            sb.Append(display).Append('\0').Append(glob).Append('\0');
        }

        sb.Append('\0');
        return sb.ToString();
    }

    private static string? DirectoryOrNull(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            if (Directory.Exists(path))
                return path;

            var dir = Path.GetDirectoryName(path);
            return !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.TrimEnd('\0');

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileName(ref OpenFileName ofn);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetSaveFileName(ref OpenFileName ofn);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string? lpstrFilter;
        public string? lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public StringBuilder? lpstrFile;
        public int nMaxFile;
        public string? lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string? lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }
}
