// ScreenStateService.cs
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;

namespace ScreenStateService
{
    public partial class ScreenStateService : ServiceBase
    {
        private ScreenStateServiceForm listenerForm;
        private Thread uiThread;

        public ScreenStateService()
        {
            ServiceName = "ScreenStateService";
            CanHandlePowerEvent = true;
        }

        protected override void OnStart(string[] args)
        {
            // Event source is created by MSI/helper
            try { EventLog.WriteEntry(ServiceName, "Service starting.", EventLogEntryType.Information); } catch { }

            uiThread = new Thread(() =>
            {
                listenerForm = new ScreenStateServiceForm(ServiceName);
                Application.Run(listenerForm);
            });
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.IsBackground = true;
            uiThread.Start();
        }

        protected override void OnStop()
        {
            try { EventLog.WriteEntry(ServiceName, "Service stopping.", EventLogEntryType.Information); } catch { }

            var form = listenerForm;
            if (form != null && !form.IsDisposed)
            {
                form.BeginInvoke(new MethodInvoker(() =>
                {
                    try { form.Close(); } catch { }
                    try { Application.ExitThread(); } catch { }
                }));
            }

            if (uiThread != null)
            {
                uiThread.Join(5000);
                uiThread = null;
            }
            listenerForm = null;
        }
    }
}
