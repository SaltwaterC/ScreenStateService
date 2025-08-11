// ScreenStateServiceForm.cs
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenStateService
{
    public class ScreenStateServiceForm : Form
    {
        private readonly string serviceName;
        private readonly Guid GUID_CONSOLE_DISPLAY_STATE = new Guid("6fe69556-704a-47a0-8f24-c28d936fda47");

        private const int WM_POWERBROADCAST = 0x0218;
        private const int PBT_POWERSETTINGCHANGE = 0x8013;
        private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct POWERBROADCAST_SETTING
        {
            public Guid PowerSetting;
            public int DataLength;
            public byte Data;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr RegisterPowerSettingNotification(
            IntPtr hRecipient,
            in Guid PowerSettingGuid, // OK in C# 7.3
            int Flags);

        public ScreenStateServiceForm(string serviceName)
        {
            this.serviceName = serviceName;
            // No Application.Run() here; the service thread does that.
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterPowerSettingNotification(this.Handle, in GUID_CONSOLE_DISPLAY_STATE, DEVICE_NOTIFY_WINDOW_HANDLE);
        }

        protected override void WndProc(ref Message msg)
        {
            if (msg.Msg == WM_POWERBROADCAST && msg.WParam.ToInt32() == PBT_POWERSETTINGCHANGE)
            {
                POWERBROADCAST_SETTING pbs = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(msg.LParam);
                if (pbs.PowerSetting == GUID_CONSOLE_DISPLAY_STATE)
                {
                    int id = pbs.Data + 1000;
                    string text;
                    switch (pbs.Data)
                    {
                        case 0:
                            text = "Screen turned off.";
                            break;
                        case 1:
                            text = "Screen turned on.";
                            break;
                        case 2:
                            text = "Screen dimmed.";
                            break;
                        default:
                            text = "Unknown screen state.";
                            break;
                    }
                    try { EventLog.WriteEntry(serviceName, text, EventLogEntryType.Information, id); } catch { }
                }
            }
            base.WndProc(ref msg);
        }
    }
}
