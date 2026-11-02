using System.Runtime.InteropServices;

namespace Guardian.Services;

/// <summary>
/// The standard Windows "pick a folder" dialog (IFileOpenDialog). Unlike the WinRT FolderPicker it also
/// works when the app runs as administrator.
/// </summary>
internal static class FolderDialog
{
    const uint FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40;
    const uint SIGDN_FILESYSPATH = 0x80058000;

    [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    class FileOpenDialogCoClass { }

    // Only the IFileDialog prefix of the vtable is declared; that is all that is used here.
    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint count, IntPtr specs);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem item);
        void SetFolder(IShellItem item);
        void GetFolder(out IShellItem item);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr bc, ref Guid handler, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint sigdn, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }

    /// <summary>Returns the chosen folder, or null if the user cancelled.</summary>
    public static string? Pick(IntPtr owner, string title) => Show(owner, title, true);

    /// <summary>Returns the chosen file, or null if the user cancelled.</summary>
    public static string? PickFile(IntPtr owner, string title) => Show(owner, title, false);

    static string? Show(IntPtr owner, string title, bool folders)
    {
        var dialog = (IFileDialog)new FileOpenDialogCoClass();
        try
        {
            dialog.GetOptions(out var options);
            dialog.SetOptions(options | (folders ? FOS_PICKFOLDERS : 0) | FOS_FORCEFILESYSTEM);
            dialog.SetTitle(title);

            if (dialog.Show(owner) != 0) return null; // cancelled (HRESULT_FROM_WIN32(ERROR_CANCELLED)) or failed

            dialog.GetResult(out var item);
            item.GetDisplayName(SIGDN_FILESYSPATH, out var ptr);
            try { return Marshal.PtrToStringUni(ptr); }
            finally { Marshal.FreeCoTaskMem(ptr); Marshal.ReleaseComObject(item); }
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }
}
