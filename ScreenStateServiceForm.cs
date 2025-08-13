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
            Guid PowerSettingGuid,
            int Flags);

        public ScreenStateServiceForm(string serviceName)
        {
            this.serviceName = serviceName;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterPowerSettingNotification(this.Handle, GUID_CONSOLE_DISPLAY_STATE, DEVICE_NOTIFY_WINDOW_HANDLE);
        }

        /// <summary>
        /// Maps console display state byte to the full EventInstance ID from the message table.
        /// Informational severity messages have the high bit set (0x40000000).
        /// </summary>
        private static long ToInstanceId(byte state)
        {
            switch (state)
            {
                case 0: return 0x400003E8; // Screen off (1000 | 0x4000_0000)
                case 1: return 0x400003E9; // Screen on  (1001 | 0x4000_0000)
                case 2: return 0x400003EA; // Screen dimmed (1002 | 0x4000_0000)
                default: return 0x400003E7; // Unknown (999 | 0x4000_0000)
            }
        }

        private void LogScreenEvent(byte state)
        {
            long instanceId = ToInstanceId(state);
            var evt = new EventInstance(instanceId, 0, EventLogEntryType.Information);
            EventLog.WriteEvent(serviceName, evt);
        }

        protected override void WndProc(ref Message msg)
        {
            if (msg.Msg == WM_POWERBROADCAST && msg.WParam.ToInt32() == PBT_POWERSETTINGCHANGE)
            {
                POWERBROADCAST_SETTING pbs = (POWERBROADCAST_SETTING)Marshal.PtrToStructure(msg.LParam, typeof(POWERBROADCAST_SETTING));
                if (pbs.PowerSetting == GUID_CONSOLE_DISPLAY_STATE)
                {
                    // Log using message table IDs (correct Event Viewer text)
                    LogScreenEvent(pbs.Data);
                }
            }
            base.WndProc(ref msg);
        }
    }
}
