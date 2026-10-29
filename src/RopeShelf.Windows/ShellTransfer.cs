using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using WpfDataObject = System.Windows.DataObject;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace RopeShelf.Windows;

public sealed record TransferResult(bool Completed, bool FolderSaved, string Evidence);

public static class ShellTransfer
{
    [ComVisible(true), Guid("00000121-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropSource
    {
        [PreserveSig] int QueryContinueDrag([MarshalAs(UnmanagedType.Bool)] bool escape, uint keys);
        [PreserveSig] int GiveFeedback(uint effect);
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class DropSource : IDropSource
    {
        public bool ExplorerAtDrop { get; private set; }
        public int QueryContinueDrag(bool escape, uint keys)
        {
            if (escape) return 0x40101;
            if ((keys & 1) == 0) { ExplorerAtDrop = Native.CursorOverExplorer(); return 0x40100; }
            return 0;
        }
        public int GiveFeedback(uint effect) => 0x40102;
    }
    [DllImport("ole32.dll", PreserveSig = true)] private static extern int DoDragDrop(ComDataObject data, IDropSource source, uint effects, out uint effect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class TrackedData(ComDataObject inner) : ComDataObject
    {
        private static readonly short Logical = unchecked((short)RegisterClipboardFormat("Logical Performed DropEffect"));
        private static readonly short Performed = unchecked((short)RegisterClipboardFormat("Performed DropEffect"));
        public uint? LogicalEffect, PerformedEffect;
        public void GetData(ref FORMATETC format, out STGMEDIUM medium) => inner.GetData(ref format, out medium);
        public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) => inner.GetDataHere(ref format, ref medium);
        public int QueryGetData(ref FORMATETC format) => inner.QueryGetData(ref format);
        public int GetCanonicalFormatEtc(ref FORMATETC input, out FORMATETC output) => inner.GetCanonicalFormatEtc(ref input, out output);
        public void SetData(ref FORMATETC format, ref STGMEDIUM medium, bool release)
        {
            if ((format.cfFormat == Logical || format.cfFormat == Performed) && medium.tymed == TYMED.TYMED_HGLOBAL)
            {
                var pointer = GlobalLock(medium.unionmember);
                if (pointer != 0)
                { try { var effect = unchecked((uint)Marshal.ReadInt32(pointer)); if (format.cfFormat == Logical) LogicalEffect = effect; else PerformedEffect = effect; } finally { GlobalUnlock(medium.unionmember); } }
            }
            inner.SetData(ref format, ref medium, release);
        }
        public IEnumFORMATETC EnumFormatEtc(DATADIR direction) => inner.EnumFormatEtc(direction);
        public int DAdvise(ref FORMATETC format, ADVF flags, IAdviseSink sink, out int connection) => inner.DAdvise(ref format, flags, sink, out connection);
        public void DUnadvise(int connection) => inner.DUnadvise(connection);
        public int EnumDAdvise(out IEnumSTATDATA? enumerator) => inner.EnumDAdvise(out enumerator);
    }
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint handle);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint handle);

    public static TransferResult Drag(string original, Storage storage)
    {
        // Transfer a separate file even for external originals; Shell may move only this copy.
        var lease = TransferStore.Create(storage.Transfers, original, DateTimeOffset.UtcNow);
        var staging = lease.FilePath;
        var data = new WpfDataObject(); data.SetFileDropList(new StringCollection { staging });
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(2))); // prefer move into folders
        var tracked = new TrackedData((ComDataObject)data);
        var source = new DropSource();
        var hr = DoDragDrop(tracked, source, 3, out var effect);
        bool completed = hr == 0x40100 && effect != 0;
        bool saved = completed && source.ExplorerAtDrop &&
            (tracked.LogicalEffect is 1 or 2 || tracked.PerformedEffect is 1 or 2 || (!File.Exists(staging) && effect == 2));
        var evidence = $"HRESULT={hr:X}; result={effect}; logical={tracked.LogicalEffect}; performed={tracked.PerformedEffect}; explorer={source.ExplorerAtDrop}; sourcePresent={File.Exists(staging)}";
        storage.Log("Drag: " + evidence);
        // Apps may read CF_HDROP paths asynchronously. Successful copies retain their staging files.
        if (!completed || saved)
        {
            try { TransferStore.Remove(storage.Transfers, lease); } catch { }
        }
        return new(completed, saved, evidence);
    }
}
