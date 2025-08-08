using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenStateService
{
    public class ScreenStateServiceForm : Form
    {
        private readonly string ServiceName;
        // Power setting GUID for display state
        readonly Guid GUID_CONSOLE_DISPLAY_STATE = new Guid("6fe69556-704a-47a0-8f24-c28d936fda47");

        // Constants
        const int WM_POWERBROADCAST = 0x0218;
        const int PBT_POWERSETTINGCHANGE = 0x8013;

        // Structs
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct POWERBROADCAST_SETTING
        {
            public Guid PowerSetting;
            public int DataLength;
            public byte Data;
        }

        // Imports
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid PowerSettingGuid, int Flags);

        const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;

        protected override void WndProc(ref Message Message)
        {
            if (Message.Msg == WM_POWERBROADCAST && Message.WParam.ToInt32() == PBT_POWERSETTINGCHANGE)
            {
                POWERBROADCAST_SETTING Pbs = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(Message.LParam);
                if (Pbs.PowerSetting == GUID_CONSOLE_DISPLAY_STATE)
                {
                    switch (Pbs.Data)
                    {
                        case 0:
                            EventLog.WriteEntry(ServiceName, "Screen turned off.", EventLogEntryType.Information, Pbs.Data + 1000);
                            break;
                        case 1:
                            EventLog.WriteEntry(ServiceName, "Screen turned on.", EventLogEntryType.Information, Pbs.Data + 1000);
                            break;
                        case 2:
                            EventLog.WriteEntry(ServiceName, "Screen dimmed.", EventLogEntryType.Information, Pbs.Data + 1000);
                            break;
                        default:
                            EventLog.WriteEntry(ServiceName, "Unknown screen state.", EventLogEntryType.Information, 999);
                            break;
                    }
                }
            }

            base.WndProc(ref Message);
        }

        public ScreenStateServiceForm(string ServiceName)
        {
            this.ServiceName = ServiceName;
            // Register for power setting notifications
            RegisterPowerSettingNotification(Handle, ref GUID_CONSOLE_DISPLAY_STATE, DEVICE_NOTIFY_WINDOW_HANDLE);
            Application.Run();
        }
    }
}
