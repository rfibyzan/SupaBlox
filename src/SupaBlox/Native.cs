using System.Runtime.InteropServices;

namespace SupaBlox;

internal static class Native
{
    public const int ERROR_ALREADY_EXISTS = 183;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateMutexW(IntPtr lpMutexAttributes, bool bInitialOwner, string lpName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateEventW(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReleaseMutex(IntPtr hMutex);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);
}
