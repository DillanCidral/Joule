using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace BatteryTray.Interop;

/// <summary>
/// Linux side of the HID layer: /dev/hidraw* nodes, described from sysfs only.
///
/// Differences from Windows that the rest of the code must not notice:
///   - one hidraw node is one USB interface, and may hold several top level
///     collections; Windows gives one path per collection. Scan returns one
///     HidCollection per collection, all with the same node, and the reader
///     keeps only the reports whose id belongs to its collection;
///   - Windows always puts the report id in byte 0 (0 when the device has no
///     numbered reports); hidraw leaves it out for unnumbered reports, so the
///     reader puts the 0 back;
///   - Windows paths carry "&amp;mi_XX" (USB interface) and some providers pick the
///     interface by it. The Linux path gets the same tag after a '#', removed
///     before the node is opened.
/// Describing never opens the node: everything comes from sysfs, so the scan
/// sends nothing to the device (Windows asks it for its strings).
/// </summary>
internal static class HidLinux
{
    internal const int O_RDONLY = 0, O_WRONLY = 1, O_RDWR = 2;
    private const int O_CLOEXEC = 0x80000;
    private const int EINTR = 4, EACCES = 13;
    private const short POLLIN = 0x1, POLLERR = 0x8, POLLHUP = 0x10, POLLNVAL = 0x20;

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd { public int Fd; public short Events; public short Revents; }

    [DllImport("libc", SetLastError = true, EntryPoint = "open")]
    private static extern int SysOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", SetLastError = true)]
    internal static extern int close(int fd);

    [DllImport("libc", SetLastError = true, EntryPoint = "read")]
    private static extern nint SysRead(int fd, byte[] buffer, nuint count);

    [DllImport("libc", SetLastError = true, EntryPoint = "write")]
    private static extern nint SysWrite(int fd, byte[] buffer, nuint count);

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    private static extern int SysIoctl(int fd, nuint request, byte[] buffer);

    [DllImport("libc", SetLastError = true, EntryPoint = "poll")]
    private static extern int SysPoll([In, Out] PollFd[] fds, nuint count, int timeoutMs);

    [DllImport("libc", SetLastError = true, EntryPoint = "eventfd")]
    private static extern int SysEventFd(uint initval, int flags);

    /// <summary>A descriptor that <see cref="Wake"/> makes readable: how Dispose stops a waiting reader at once, as CancelIoEx does on Windows.</summary>
    internal static int NewWaker() => SysEventFd(0, O_CLOEXEC);

    internal static void Wake(int waker) => SysWrite(waker, BitConverter.GetBytes(1UL), 8);

    [DllImport("libc", SetLastError = true, EntryPoint = "realpath")]
    private static extern IntPtr SysRealPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr resolved);

    [DllImport("libc", EntryPoint = "free")]
    private static extern void SysFree(IntPtr p);

    /// <summary>realpath(3). sysfs chains relative symlinks, which ResolveLinkTarget resolves against the wrong directory.</summary>
    internal static string? RealPath(string path)
    {
        IntPtr p = SysRealPath(path, IntPtr.Zero);
        if (p == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUTF8(p); }
        finally { SysFree(p); }
    }

    /// <summary>Nodes that refused to open for lack of permission (no udev rule): the CLI explains how to fix it.</summary>
    internal static readonly ConcurrentDictionary<string, byte> Denied = new();

    /// <summary>The device node of a collection path ("/dev/hidraw3#&amp;mi_02" -> "/dev/hidraw3").</summary>
    internal static string Node(string path) => path.Split('#')[0];

    internal static int Open(string path, int flags)
    {
        string node = Node(path);
        int fd = SysOpen(node, flags | O_CLOEXEC);
        if (fd < 0 && Marshal.GetLastPInvokeError() == EACCES) Denied.TryAdd(node, 0);
        return fd;
    }

    /// <summary>
    /// Waits for the next report and reads it. 0 = interrupted by a signal, try
    /// again; -1 = device gone, error, or <paramref name="waker"/> was woken.
    /// </summary>
    internal static int Read(int fd, int waker, byte[] buffer)
    {
        var p = new[] { new PollFd { Fd = fd, Events = POLLIN }, new PollFd { Fd = waker, Events = POLLIN } };
        int r = SysPoll(p, 2, -1);
        if (r < 0) return Marshal.GetLastPInvokeError() == EINTR ? 0 : -1;
        if (p[1].Revents != 0 || (p[0].Revents & (POLLERR | POLLHUP | POLLNVAL)) != 0) return -1;
        nint got = SysRead(fd, buffer, (nuint)buffer.Length);
        return got > 0 ? (int)got : -1;
    }

    // shortcut: a blocked write waits for the kernel's own USB timeout (5 s) instead of the 1 s Windows uses; only a stuck device hits it.
    internal static bool Write(int fd, byte[] buffer) => SysWrite(fd, buffer, (nuint)buffer.Length) == buffer.Length;

    // _IOC(_IOC_READ | _IOC_WRITE, 'H', nr, len) from linux/hidraw.h
    private static nuint Ioc(uint nr, int len) => (nuint)((3u << 30) | ((uint)len << 16) | ('H' << 8) | nr);

    /// <summary>HIDIOCSFEATURE: byte 0 is the report id, as on Windows.</summary>
    internal static bool SetFeature(int fd, byte[] buffer) => SysIoctl(fd, Ioc(0x06, buffer.Length), buffer) >= 0;

    /// <summary>HIDIOCGFEATURE: byte 0 is the report id in and out, as on Windows.</summary>
    internal static bool GetFeature(int fd, byte[] buffer) => SysIoctl(fd, Ioc(0x07, buffer.Length), buffer) >= 0;

    /// <summary>
    /// Turns what hidraw returned into what Windows would have returned: the
    /// report id in byte 0 and the length of the longest input report. Null =
    /// the report belongs to another collection of the same node.
    /// </summary>
    internal static byte[]? AsWindowsReport(HidCollection c, byte[] buffer, int got)
    {
        bool numbered = c.ReportIds.Length > 0;
        if (numbered && Array.IndexOf(c.ReportIds, buffer[0]) < 0) return null;
        int offset = numbered ? 0 : 1;
        var report = new byte[Math.Max(c.InputLen, got + offset)];
        Array.Copy(buffer, 0, report, offset, got);
        return report;
    }

    public static List<HidCollection> Scan()
    {
        var result = new List<HidCollection>();
        const string root = "/sys/class/hidraw";
        if (!Directory.Exists(root)) return result;
        // sysfs class entries are symlinks, which GetDirectories leaves out.
        foreach (var dir in Directory.GetFileSystemEntries(root).OrderBy(d => d.Length).ThenBy(d => d, StringComparer.Ordinal))
            result.AddRange(Describe("/dev/" + System.IO.Path.GetFileName(dir)));
        return result;
    }

    /// <summary>Every collection of one hidraw node, from sysfs. Empty if the node is gone or unreadable.</summary>
    public static List<HidCollection> Describe(string path)
    {
        var result = new List<HidCollection>();
        string name = System.IO.Path.GetFileName(Node(path));
        try
        {
            var hid = RealPath($"/sys/class/hidraw/{name}/device");
            if (hid is null) return result;

            // HID_ID=0003:0000046D:0000C52B, HID_NAME=Logitech USB Receiver
            var uevent = File.ReadAllLines(System.IO.Path.Combine(hid, "uevent"))
                .Select(l => l.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
            var id = uevent.GetValueOrDefault("HID_ID", "").Split(':');
            if (id.Length != 3) return result;
            ushort vid = (ushort)Convert.ToUInt32(id[1], 16), pid = (ushort)Convert.ToUInt32(id[2], 16);

            // USB strings and interface number: the interface is the HID device's parent, the USB device its grandparent.
            string? iface = Directory.GetParent(hid)?.FullName;
            string? usb = iface is null ? null : Directory.GetParent(iface)?.FullName;
            bool isUsb = usb is not null && File.Exists(System.IO.Path.Combine(usb, "idVendor"));
            string product = (isUsb ? Sys(usb!, "product") : null) ?? uevent.GetValueOrDefault("HID_NAME", "");
            string manufacturer = (isUsb ? Sys(usb!, "manufacturer") : null) ?? "";
            ushort version = isUsb && Sys(usb!, "bcdDevice") is { } bcd ? (ushort)Convert.ToUInt32(bcd, 16) : (ushort)0;
            string tag = isUsb && Sys(usb!, "bNumInterfaces") is { } n && int.Parse(n) > 1 && Sys(iface!, "bInterfaceNumber") is { } mi
                ? $"#&mi_{Convert.ToInt32(mi, 16):x2}" : "";

            // sysfs says 4096 bytes and holds fewer: ReadAllBytes trusts the size and throws, so copy until EOF.
            using var file = File.OpenRead(System.IO.Path.Combine(hid, "report_descriptor"));
            using var bytes = new MemoryStream();
            file.CopyTo(bytes);
            var descriptor = bytes.ToArray();
            foreach (var t in ParseDescriptor(descriptor))
            {
                result.Add(new HidCollection
                {
                    Path = $"/dev/{name}{tag}",
                    Vid = vid,
                    Pid = pid,
                    Version = version,
                    UsagePage = t.Page,
                    Usage = t.Usage,
                    InputLen = t.InputLen,
                    OutputLen = t.OutputLen,
                    FeatureLen = t.FeatureLen,
                    ReportIds = t.InputIds,
                    Product = product.Trim(),
                    Manufacturer = manufacturer.Trim(),
                });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or OverflowException)
        {
            // Node left between listing and reading, or sysfs we do not understand: skip it.
        }
        return result;
    }

    private static string? Sys(string dir, string file)
    {
        try { return File.ReadAllText(System.IO.Path.Combine(dir, file)).Trim(); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    internal sealed record Tlc(ushort Page, ushort Usage, int InputLen, int OutputLen, int FeatureLen, byte[] InputIds);

    /// <summary>
    /// Top level collections of a HID report descriptor, with the report
    /// lengths Windows would give (HidP_GetCaps): longest report of each kind
    /// in bytes, plus one for the report id, or 0 if the collection has none.
    /// </summary>
    internal static List<Tlc> ParseDescriptor(ReadOnlySpan<byte> d)
    {
        const int Input = 0x8, Output = 0x9, Feature = 0xB;
        var tlcs = new List<Tlc>();
        var stack = new Stack<(uint Page, uint Size, uint Count, byte Id)>();
        uint page = 0, size = 0, count = 0;
        byte id = 0;
        uint? usage = null;
        bool usageExtended = false;
        int depth = 0;
        Dictionary<(int Kind, byte Id), uint>? bits = null;
        ushort tPage = 0, tUsage = 0;

        for (int i = 0; i < d.Length;)
        {
            byte prefix = d[i];
            if (prefix == 0xFE) // long item: skip it whole
            {
                if (i + 1 >= d.Length) break;
                i += 3 + d[i + 1];
                continue;
            }
            int n = (prefix & 3) == 3 ? 4 : prefix & 3;
            if (i + 1 + n > d.Length) break;
            uint v = 0;
            for (int k = 0; k < n; k++) v |= (uint)d[i + 1 + k] << (8 * k);
            int type = (prefix >> 2) & 3, tag = prefix >> 4;
            i += 1 + n;

            switch (type)
            {
                case 0: // main
                    if (tag == 0xA && depth++ == 0)
                    {
                        uint u = usage ?? 0;
                        tPage = usageExtended ? (ushort)(u >> 16) : (ushort)page;
                        tUsage = (ushort)u;
                        bits = new();
                    }
                    else if (tag == 0xC && depth > 0 && --depth == 0 && bits is not null)
                    {
                        int Len(int kind) => bits.Where(b => b.Key.Kind == kind).Select(b => (int)((b.Value + 7) / 8)).DefaultIfEmpty(-1).Max() + 1;
                        var ids = bits.Keys.Where(k => k.Kind == Input && k.Id != 0).Select(k => k.Id).Distinct().ToArray();
                        tlcs.Add(new Tlc(tPage, tUsage, Len(Input), Len(Output), Len(Feature), ids));
                        bits = null;
                    }
                    else if (tag is Input or Output or Feature && bits is not null)
                    {
                        bits[(tag, id)] = bits.GetValueOrDefault((tag, id)) + size * count;
                    }
                    usage = null; // locals reset after every main item
                    usageExtended = false;
                    break;
                case 1: // global
                    switch (tag)
                    {
                        case 0x0: page = v; break;
                        case 0x7: size = v; break;
                        case 0x8: id = (byte)v; break;
                        case 0x9: count = v; break;
                        case 0xA: stack.Push((page, size, count, id)); break;
                        case 0xB: if (stack.Count > 0) (page, size, count, id) = stack.Pop(); break;
                    }
                    break;
                case 2: // local: the first usage names the collection
                    if (tag == 0x0 && usage is null) { usage = v; usageExtended = n == 4; }
                    break;
            }
        }
        return tlcs;
    }
}
