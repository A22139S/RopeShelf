using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RopeShelf.Windows;

internal static class ParentRecycleProbe
{
    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint context, in Guid handler, in Guid iid, out nint result);
        void GetParent(out nint result);
        void GetDisplayName(uint kind, out nint text);
    }
    [ComImport, Guid("00ADB003-BDE9-45C6-8E29-D09F9353E108"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITransferSource
    {
        void Advise(nint sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetProperties(nint properties);
        void OpenItem(nint item, uint flags, in Guid iid, out nint result);
        void MoveItem(nint item, nint destination, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out nint result);
        [PreserveSig] int RecycleItem(nint source, nint destination, uint flags, out nint result);
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, nint context, in Guid iid, out nint item);
    [DllImport("shell32.dll", PreserveSig = false)]
    private static extern void SHGetKnownFolderItem(in Guid folder, uint flags, nint token, in Guid iid, out nint item);

    public static int Run(string root)
    {
        var store = new Storage(Path.Combine(root, "parent-recycle-" + Guid.NewGuid().ToString("N")));
        var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgr32, null, new byte[] { 20, 80, 140, 255 }, 4);
        var bytes = ImageCodec.FromBitmap(image).Png;
        var path = store.StoreImage(bytes); var backup = path + ".probe-backup"; File.Copy(path, backup);
        var shellIid = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
        var binId = new Guid("B7534046-3ECB-4C18-BE4E-64CD4CB7D6AC");
        var handler = new Guid("D5E346A1-F753-4932-B403-4574800E2498");
        var transferIid = new Guid("00ADB003-BDE9-45C6-8E29-D09F9353E108");
        nint source = 0, parent = 0, destination = 0, transferPointer = 0, recycled = 0;
        object? sourceObject = null, parentObject = null, transferObject = null, recycledObject = null;
        int? hr = null; bool payloadMatches = false, leftSource = false; string error = "";
        try
        {
            SHCreateItemFromParsingName(path, 0, in shellIid, out source);
            sourceObject = Marshal.GetObjectForIUnknown(source);
            ((IShellItem)sourceObject).GetParent(out parent);
            parentObject = Marshal.GetObjectForIUnknown(parent);
            ((IShellItem)parentObject).BindToHandler(0, in handler, in transferIid, out transferPointer);
            transferObject = Marshal.GetObjectForIUnknown(transferPointer);
            SHGetKnownFolderItem(in binId, 0, 0, in shellIid, out destination);
            hr = ((ITransferSource)transferObject).RecycleItem(source, destination, 0, out recycled);
            leftSource = !File.Exists(path);
            Marshal.ThrowExceptionForHR(hr.Value);
            if (recycled != 0)
            {
                recycledObject = Marshal.GetObjectForIUnknown(recycled);
                ((IShellItem)recycledObject).GetDisplayName(0x80058000, out var text);
                try
                {
                    var recycledPath = Marshal.PtrToStringUni(text)!;
                    payloadMatches = File.ReadAllBytes(recycledPath).SequenceEqual(bytes);
                }
                finally { Marshal.FreeCoTaskMem(text); }
            }
        }
        catch (Exception e) { error = e.Message; }
        finally
        {
            foreach (var obj in new[] { recycledObject, transferObject, parentObject, sourceObject })
                if (obj != null) Marshal.ReleaseComObject(obj);
            foreach (var pointer in new[] { recycled, transferPointer, destination, parent, source })
                if (pointer != 0) Marshal.Release(pointer);
        }
        bool passed = hr >= 0 && recycled != 0 && leftSource && payloadMatches;
        if (!passed && !File.Exists(path)) File.Copy(backup, path);
        var result = new { Passed = passed, HResult = hr?.ToString("X"), ReturnedDestination = recycled != 0,
            LeftSource = leftSource, PayloadMatches = payloadMatches, SourceRestoredOnFailure = !passed && File.ReadAllBytes(path).SequenceEqual(bytes), Error = error };
        File.WriteAllText(Path.Combine(root, "parent-recycle-results.json"), JsonSerializer.Serialize(result));
        Console.WriteLine(JsonSerializer.Serialize(result)); return passed ? 0 : 1;
    }
}
