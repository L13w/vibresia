#if WINDOWS
using System.Runtime.InteropServices;
using Vibresia.Core;

namespace Vibresia.App;

/// <summary>
/// What the app asks of, and reads from, Windows itself: keeping the
/// display awake while a vibescape plays (when the user turns that on), noticing when Windows dims or
/// turns off the display anyway (so a dark screen can be explained from the
/// log), how long since the last keyboard or mouse input, and whether the
/// user has turned animation effects off (the Mac's Reduce Motion).
/// </summary>
internal sealed partial class WindowsSystem : IDisposable
{
    private const uint EsContinuous = 0x80000000, EsSystemRequired = 0x00000001, EsDisplayRequired = 0x00000002;
    private const uint SpiGetClientAreaAnimation = 0x1042;
    private const uint DeviceNotifyCallback = 2;
    private static readonly Guid ConsoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    private readonly Log _log;
    private readonly DeviceNotifyCallbackRoutine _callback; // held so the GC can't collect it while Windows calls it
    private readonly GCHandle _self;
    private IntPtr _registration;

    public WindowsSystem(Log log)
    {
        _log = log;
        _callback = OnPowerSetting;
        _self = GCHandle.Alloc(this);
    }

    /// <summary>
    /// Asks Windows not to dim or turn off the display while the app runs, or
    /// stops asking. An option, off by default as on the Mac (whose source
    /// makes no such request); the user turns it on from the settings menu.
    /// </summary>
    public void KeepDisplayAwake(bool on)
    {
        var previous = SetThreadExecutionState(on ? EsContinuous | EsSystemRequired | EsDisplayRequired : EsContinuous);
        _log.Info("power", previous == 0 ? $"couldn't change whether Windows keeps the display awake (asked: {on})"
            : on ? "asked Windows to keep the display awake while running" : "letting Windows dim and sleep the display as usual");
    }

    /// <summary>Logs each time Windows turns the display off, dims it, or turns it on.</summary>
    public unsafe void WatchDisplay()
    {
        var parameters = new DeviceNotifySubscribeParameters
        {
            Callback = Marshal.GetFunctionPointerForDelegate(_callback),
            Context = GCHandle.ToIntPtr(_self),
        };
        var guid = ConsoleDisplayState;
        // Copied by Windows during the call, so a stack copy is enough.
        var error = PowerSettingRegisterNotification(ref guid, DeviceNotifyCallback, (IntPtr)(&parameters), out _registration);
        if (error != 0) _log.Warn("power", $"couldn't watch the display's power state (error {error})");
    }

    private delegate uint DeviceNotifyCallbackRoutine(IntPtr context, uint type, IntPtr setting);

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceNotifySubscribeParameters
    {
        public IntPtr Callback;
        public IntPtr Context;
    }

    private const uint PbtPowerSettingChange = 0x8013;

    private uint OnPowerSetting(IntPtr context, uint type, IntPtr setting)
    {
        if (type != PbtPowerSettingChange || setting == IntPtr.Zero) return 0;
        // POWERBROADCAST_SETTING: GUID (16 bytes), DWORD length, then the data (a DWORD here).
        var state = Marshal.ReadInt32(setting, 20);
        _log.Info("power", "Windows turned the display " + state switch { 0 => "off", 1 => "on", 2 => "dim", _ => $"to state {state}" } +
                           $"; input idle {InputIdleSeconds():F0} s");
        return 0;
    }

    /// <summary>Seconds since the last keyboard or mouse input anywhere on this PC.</summary>
    public static double InputIdleSeconds()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return -1;
        return unchecked((uint)Environment.TickCount - info.Time) / 1000.0;
    }

    /// <summary>Whether the user has turned off "Animation effects" (Settings → Accessibility → Visual effects).</summary>
    public static bool AnimationsOff()
    {
        var on = 1;
        return SystemParametersInfoW(SpiGetClientAreaAnimation, 0, ref on, 0) && on == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint SetThreadExecutionState(uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LastInputInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoW(uint action, uint param, ref int value, uint winIni);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSettingRegisterNotification(ref Guid setting, uint flags, IntPtr recipient, out IntPtr registration);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSettingUnregisterNotification(IntPtr registration);

    public void Dispose()
    {
        if (_registration != IntPtr.Zero) PowerSettingUnregisterNotification(_registration);
        _registration = IntPtr.Zero;
        SetThreadExecutionState(EsContinuous);
        if (_self.IsAllocated) _self.Free();
    }
}
#endif
