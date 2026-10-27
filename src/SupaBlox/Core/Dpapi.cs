using System.Runtime.InteropServices;
using System.Text;

namespace SupaBlox.Core;

/// <summary>Enkripsi cookie dengan DPAPI (CurrentUser): hanya akun Windows ini yang bisa membukanya.</summary>
public static class Dpapi
{
    public static string Protect(string plain)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI hanya ada di Windows.");
        return Convert.ToBase64String(Run(Encoding.UTF8.GetBytes(plain), protect: true));
    }

    public static string Unprotect(string base64)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI hanya ada di Windows.");
        return Encoding.UTF8.GetString(Run(Convert.FromBase64String(base64), protect: false));
    }

    private static byte[] Run(byte[] data, bool protect)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var input = new Native.DATA_BLOB { cbData = data.Length, pbData = handle.AddrOfPinnedObject() };
            bool ok = protect
                ? Native.CryptProtectData(ref input, "SupaBlox", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, Native.CRYPTPROTECT_UI_FORBIDDEN, out var output)
                : Native.CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, Native.CRYPTPROTECT_UI_FORBIDDEN, out output);
            if (!ok) throw new InvalidOperationException($"DPAPI gagal (Win32 {Marshal.GetLastWin32Error()}).");
            try
            {
                var result = new byte[output.cbData];
                Marshal.Copy(output.pbData, result, 0, output.cbData);
                return result;
            }
            finally { Native.LocalFree(output.pbData); }
        }
        finally { handle.Free(); }
    }
}
