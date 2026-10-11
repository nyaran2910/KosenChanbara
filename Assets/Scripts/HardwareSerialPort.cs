using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

/// <summary>Native desktop serial transport, without a System.IO.Ports package dependency.</summary>
internal sealed class HardwareSerialPort : IDisposable
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    // fBinary, DTR_CONTROL_ENABLE (bits 4-5), RTS_CONTROL_ENABLE (bits 12-13).
    // Arduino ESP's USBCDC implementation needs DTR and RTS to report connected.
    // https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-dcb
    private const uint SerialControlFlags = 1u | (1u << 4) | (1u << 12);
    private IntPtr handle = new IntPtr(-1);
    [StructLayout(LayoutKind.Sequential)] private struct Dcb
    {
        public uint Length, BaudRate, Flags;
        public ushort Reserved, XonLimit, XoffLimit;
        public byte ByteSize, Parity, StopBits, XonChar, XoffChar, ErrorChar, EofChar, EvtChar;
        public ushort Reserved1;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Timeouts { public uint Interval, ReadMultiplier, ReadConstant, WriteMultiplier, WriteConstant; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetCommState(IntPtr handle, ref Dcb dcb);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetCommState(IntPtr handle, ref Dcb dcb);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetCommTimeouts(IntPtr handle, ref Timeouts timeouts);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadFile(IntPtr handle, byte[] bytes, uint count, out uint read, IntPtr overlapped);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
#else
    private int descriptor = -1;
    [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern IntPtr read(int descriptor, byte[] buffer, UIntPtr size);
    [DllImport("libc")] private static extern int close(int descriptor);
#endif

    public HardwareSerialPort(string path)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        handle = CreateFile("\\\\.\\" + path, 0x80000000u | 0x40000000u, 0, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle == new IntPtr(-1)) throw new IOException("Could not open " + path);
        Dcb dcb = new Dcb { Length = (uint)Marshal.SizeOf(typeof(Dcb)) };
        if (!GetCommState(handle, ref dcb)) { Dispose(); throw new IOException("Could not read serial settings"); }
        dcb.BaudRate = 115200; dcb.Flags = SerialControlFlags; dcb.ByteSize = 8; dcb.Parity = 0; dcb.StopBits = 0;
        Timeouts timeouts = new Timeouts { Interval = uint.MaxValue, ReadConstant = 10 };
        if (!SetCommState(handle, ref dcb) || !SetCommTimeouts(handle, ref timeouts)) { Dispose(); throw new IOException("Could not configure serial settings"); }
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        string flag;
        int openFlags;
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        flag = "-f"; openFlags = 2 | 4 | 0x20000;
#else
        flag = "-F"; openFlags = 2 | 0x800 | 0x100;
#endif
        descriptor = open(path, openFlags);
        if (descriptor < 0) throw new IOException("Could not open " + path + " (errno " + Marshal.GetLastWin32Error() + ")");
        try
        {
            ProcessStartInfo settings = new ProcessStartInfo("/bin/stty", flag + " \"" + path.Replace("\"", "\\\"") + "\" 115200 raw -echo -ixon -ixoff -crtscts")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            using (Process process = Process.Start(settings))
            {
                if (!process.WaitForExit(2000)) { process.Kill(); throw new IOException("Serial configuration timed out"); }
                if (process.ExitCode != 0) throw new IOException(process.StandardError.ReadToEnd().Trim());
            }
        }
        catch { Dispose(); throw; }
#else
        throw new PlatformNotSupportedException("USB controllers require a desktop player.");
#endif
    }

    public int Read(byte[] bytes)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!ReadFile(handle, bytes, (uint)bytes.Length, out uint count, IntPtr.Zero)) throw new IOException("Serial read failed");
        return (int)count;
#else
        long count = read(descriptor, bytes, new UIntPtr((uint)bytes.Length)).ToInt64();
        if (count >= 0) return (int)count;
        int error = Marshal.GetLastWin32Error();
        if (error == 11 || error == 35 || error == 4) return 0; // EAGAIN / EINTR
        throw new IOException("Serial read failed (errno " + error + ")");
#endif
    }

    public void Dispose()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (handle != new IntPtr(-1)) { CloseHandle(handle); handle = new IntPtr(-1); }
#else
        if (descriptor >= 0) { close(descriptor); descriptor = -1; }
#endif
    }
}
