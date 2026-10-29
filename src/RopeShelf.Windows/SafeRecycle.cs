using System.Runtime.InteropServices;

namespace RopeShelf.Windows;

/// <summary>Requests recycling, confirms a Recycle Bin destination and keeps a recovery copy until confirmed.</summary>
public static class SafeRecycle
{
    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(IProgressSink sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(nint dialog);
        void SetProperties(nint properties);
        void SetOwnerWindow(uint hwnd);
        void ApplyPropertiesToItem(nint item);
        void ApplyPropertiesToItems(nint items);
        void RenameItem(nint item, [MarshalAs(UnmanagedType.LPWStr)] string name, IProgressSink sink);
        void RenameItems(nint items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        void MoveItem(nint item, nint destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IProgressSink sink);
        void MoveItems(nint items, nint destination);
        void CopyItem(nint item, nint destination, [MarshalAs(UnmanagedType.LPWStr)] string name, IProgressSink sink);
        void CopyItems(nint items, nint destination);
        void DeleteItem(nint item, IProgressSink sink);
        void DeleteItems(nint items);
        void NewItem(nint destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, IProgressSink sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
    [ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, nint item, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostRenameItem(uint flags, nint item, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, nint created);
        [PreserveSig] int PreMoveItem(uint flags, nint item, nint dest, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostMoveItem(uint flags, nint item, nint dest, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, nint created);
        [PreserveSig] int PreCopyItem(uint flags, nint item, nint dest, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostCopyItem(uint flags, nint item, nint dest, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, nint created);
        [PreserveSig] int PreDeleteItem(uint flags, nint item);
        [PreserveSig] int PostDeleteItem(uint flags, nint item, int result, nint created);
        [PreserveSig] int PreNewItem(uint flags, nint dest, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostNewItem(uint flags, nint dest, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, uint attributes, int result, nint created);
        [PreserveSig] int UpdateProgress(uint total, uint current);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class Sink : IProgressSink
    {
        public bool Recycled;
        public List<string> Events { get; } = [];
        public int StartOperations() => 0;
        public int FinishOperations(int result) => 0;
        public int PreRenameItem(uint flags, nint item, string name) => 0;
        public int PostRenameItem(uint flags, nint item, string name, int result, nint created) => 0;
        public int PreMoveItem(uint flags, nint item, nint dest, string name) => 0;
        public int PostMoveItem(uint flags, nint item, nint dest, string name, int result, nint created) => 0;
        public int PreCopyItem(uint flags, nint item, nint dest, string name) => 0;
        public int PostCopyItem(uint flags, nint item, nint dest, string name, int result, nint created) => 0;
        public int PreDeleteItem(uint flags, nint item) { Events.Add($"PreDelete flags={flags:X}"); return 0; }
        public int PostDeleteItem(uint flags, nint item, int result, nint created) { Recycled = WasRecycled(result, created); Events.Add($"PostDelete flags={flags:X}; hr={result:X}; destination={created != 0}"); return 0; }
        public int PreNewItem(uint flags, nint dest, string name) => 0;
        public int PostNewItem(uint flags, nint dest, string name, string template, uint attributes, int result, nint created) => 0;
        public int UpdateProgress(uint total, uint current) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }
    // PostDeleteItem's newly-created IShellItem is documented as the item now in the Recycle Bin.
    public static bool WasRecycled(int result, nint destination) => result >= 0 && destination != 0;
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, nint context, in Guid iid, out nint item);

    public static void File(string path, Action<string>? log = null)
    {
        var backup = path + ".recycle-backup";
        System.IO.File.Copy(path, backup, false);
        try { RecycleCore(path, log); System.IO.File.Delete(backup); }
        catch
        {
            if (!System.IO.File.Exists(path)) System.IO.File.Move(backup, path);
            else System.IO.File.Delete(backup);
            throw;
        }
    }
    private static void RecycleCore(string path, Action<string>? log)
    {
        var shellItem = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
        SHCreateItemFromParsingName(Path.GetFullPath(path), 0, in shellItem, out var item);
        var operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"))!)!;
        try
        {
            operation.SetOperationFlags(0x00080000 | 0x00100000 | 0x20000000 | 0x0004 | 0x0010 | 0x0400);
            var sink = new Sink(); operation.DeleteItem(item, sink); operation.PerformOperations();
            operation.GetAnyOperationsAborted(out var aborted);
            log?.Invoke($"Recycle: {string.Join(';', sink.Events)}; aborted={aborted}; sourceExists={System.IO.File.Exists(path)}");
            if (aborted || !sink.Recycled || System.IO.File.Exists(path)) throw new IOException("回收站未确认接收，已保留待处理记录");
        }
        finally { Marshal.Release(item); Marshal.FinalReleaseComObject(operation); }
    }
}
