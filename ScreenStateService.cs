using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;

namespace ScreenStateService {
    public sealed class ScreenStateService : ServiceBase
    {
        // Power
        const int WM_POWERBROADCAST = 0x0218;
        const int PBT_POWERSETTINGCHANGE = 0x8013;
        static Guid GUID_CONSOLE_DISPLAY_STATE = new Guid("6fe69556-704a-47a0-8f24-c28d936fda47");

        // Win32
        const int CS_OWNDC = 0x20;
        const uint WM_CLOSE = 0x0010;
        static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct POWERBROADCAST_SETTING { public Guid PowerSetting; public int DataLength; public byte Data; }

        delegate IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASSEX
        {
            public uint cbSize; public uint style; public WndProc lpfnWndProc; public int cbClsExtra; public int cbWndExtra;
            public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x; public int y; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateWindowEx(
            int exStyle, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] static extern sbyte GetMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG lpMsg);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG lpMsg);
        [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid guid, int flags);
        [DllImport("user32.dll", SetLastError = true)] static extern bool UnregisterPowerSettingNotification(IntPtr handle);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern void PostQuitMessage(int nExitCode);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string lpModuleName);

        Thread _thread;
        IntPtr _hwnd = IntPtr.Zero, _notify = IntPtr.Zero;
        WndProc _proc; // keep delegate alive

        public ScreenStateService()
        {
            ServiceName = "ScreenStateService";
            CanHandlePowerEvent = true; // lets ServiceBase set SERVICE_ACCEPT_POWEREVENT
        }

        protected override void OnStart(string[] _) // start message loop
        {
            try { EventLog.WriteEntry(ServiceName, "Service starting.", EventLogEntryType.Information); } catch { }

            _thread = new Thread(MessagePump) { IsBackground = true, Name = "ScreenStateListener" };
            _thread.SetApartmentState(ApartmentState.MTA); // message-only window is fine on MTA
            _thread.Start();
        }

        protected override void OnStop()
        {
            try { EventLog.WriteEntry(ServiceName, "Service stopping.", EventLogEntryType.Information); } catch { }

            var hwnd = _hwnd;
            if (hwnd != IntPtr.Zero) PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

            if (_thread != null) _thread.Join(5000);
            _thread = null; _hwnd = IntPtr.Zero;
        }

        void MessagePump()
        {
            _proc = WndProcThunk;

            var wc = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = _proc, lpszClassName = "ScreenStateSvcMsgOnly", style = CS_OWNDC };
            wc.hInstance = GetModuleHandle(null);
            if (RegisterClassEx(ref wc) == 0) return;

            _hwnd = CreateWindowEx(0, wc.lpszClassName, "ScreenStateSvcMsgOnly", 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) return;

            _notify = RegisterPowerSettingNotification(_hwnd, ref GUID_CONSOLE_DISPLAY_STATE, 0 /* DEVICE_NOTIFY_WINDOW_HANDLE */);

            while (GetMessage(out MSG m, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref m);
                DispatchMessage(ref m);
            }

            if (_notify != IntPtr.Zero) { try { UnregisterPowerSettingNotification(_notify); } catch { } _notify = IntPtr.Zero; }
        }

        IntPtr WndProcThunk(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            if (msg == WM_CLOSE)
            {
                DestroyWindow(h);
                return IntPtr.Zero;
            }
            if (msg == 0x0002 /* WM_DESTROY */)
            {
                if (_notify != IntPtr.Zero) { try { UnregisterPowerSettingNotification(_notify); } catch { } _notify = IntPtr.Zero; }
                PostQuitMessage(0);
                return IntPtr.Zero;
            }

            if (msg == WM_POWERBROADCAST && w.ToInt32() == PBT_POWERSETTINGCHANGE && l != IntPtr.Zero)
            {
                var pbs = (POWERBROADCAST_SETTING)Marshal.PtrToStructure(l, typeof(POWERBROADCAST_SETTING));
                if (pbs.PowerSetting == GUID_CONSOLE_DISPLAY_STATE)
                {
                    int id = pbs.Data + 1000;
                    switch (pbs.Data)
                    {
                        case 0:
                            EventLog.WriteEntry(ServiceName, "Screen turned off.", EventLogEntryType.Information, id);
                            break;
                        case 1:
                            EventLog.WriteEntry(ServiceName, "Screen turned on.", EventLogEntryType.Information, id);
                            break;
                        case 2:
                            EventLog.WriteEntry(ServiceName, "Screen dimmed.", EventLogEntryType.Information, id);
                            break;
                        default:
                            EventLog.WriteEntry(ServiceName, "Unknown screen state.", EventLogEntryType.Information, 999);
                            break;
                    }
                }
            }
            return DefWindowProc(h, msg, w, l);
        }
    }
}
