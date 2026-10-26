using System.Runtime.InteropServices;

namespace SupaBlox;

/// <summary>
/// Memegang objek singleton Roblox (ROBLOX_singletonMutex + ROBLOX_singletonEvent)
/// di SATU thread khusus yang hidup selama fitur aktif.
/// Kepemilikan mutex terikat ke thread: kalau thread pemilik mati, mutex jadi
/// "abandoned" dan Roblox bisa merebutnya lagi. Karena itu acquire dan release
/// dilakukan di thread yang sama.
/// Mode mutex-only: tidak menyentuh proses Roblox sama sekali (tanpa
/// DuplicateHandle, tanpa WriteProcessMemory, tanpa injeksi).
/// </summary>
public sealed class SingletonHolder : IDisposable
{
    public const string MutexName = "ROBLOX_singletonMutex";
    public const string EventName = "ROBLOX_singletonEvent";

    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ManualResetEventSlim _stop = new(false);
    private Thread? _thread;

    public bool MutexOwned { get; private set; }
    public bool MutexExisted { get; private set; }
    public bool EventHeld { get; private set; }
    public bool EventExisted { get; private set; }
    public string? Error { get; private set; }

    public bool IsActive => MutexOwned || EventHeld;

    public bool Start(TimeSpan timeout)
    {
        if (_thread != null) return IsActive;
        _thread = new Thread(HoldLoop) { IsBackground = true, Name = "supablox-singleton-holder" };
        _thread.Start();
        if (!_ready.Wait(timeout))
        {
            Error = "Thread holder tidak merespons tepat waktu.";
            return false;
        }
        return IsActive;
    }

    private void HoldLoop()
    {
        IntPtr mutex = IntPtr.Zero, evt = IntPtr.Zero;
        try
        {
            mutex = Native.CreateMutexW(IntPtr.Zero, true, MutexName);
            int mErr = Marshal.GetLastWin32Error();
            if (mutex != IntPtr.Zero)
            {
                MutexExisted = mErr == Native.ERROR_ALREADY_EXISTS;
                // Kalau mutex sudah ada (milik Roblox yang sedang jalan), bInitialOwner diabaikan:
                // kita tidak memilikinya, tapi handle tetap dipegang agar objek tetap hidup.
                MutexOwned = !MutexExisted;
            }

            evt = Native.CreateEventW(IntPtr.Zero, true, false, EventName);
            int eErr = Marshal.GetLastWin32Error();
            if (evt != IntPtr.Zero)
            {
                EventHeld = true;
                EventExisted = eErr == Native.ERROR_ALREADY_EXISTS;
            }

            if (mutex == IntPtr.Zero && evt == IntPtr.Zero)
                Error = $"Windows menolak membuat objek singleton (Win32 error {mErr}/{eErr}).";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            _ready.Set();
        }

        _stop.Wait();

        if (mutex != IntPtr.Zero)
        {
            if (MutexOwned) Native.ReleaseMutex(mutex);
            Native.CloseHandle(mutex);
        }
        if (evt != IntPtr.Zero) Native.CloseHandle(evt);
        MutexOwned = false;
        EventHeld = false;
    }

    public void Dispose()
    {
        _stop.Set();
        _thread?.Join(TimeSpan.FromSeconds(3));
        _ready.Dispose();
        _stop.Dispose();
    }
}
