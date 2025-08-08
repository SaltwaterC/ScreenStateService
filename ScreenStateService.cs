using System.Diagnostics;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;

namespace ScreenStateService
{
    public partial class ScreenStateService : ServiceBase
    {
        private ScreenStateServiceForm ListenerForm;
        private Thread MessageLoopThread;

        public ScreenStateService()
        {
            ServiceName = "ScreenStateService";
            CanHandlePowerEvent = true;
        }

        protected override void OnStart(string[] args)
        {
            if (!EventLog.SourceExists(ServiceName))
            {
                EventLog.CreateEventSource(ServiceName, "Application");
            }
            EventLog.WriteEntry(ServiceName, "Service starting.", EventLogEntryType.Information);

            MessageLoopThread = new Thread(() =>
            {
                ListenerForm = new ScreenStateServiceForm(ServiceName);
            });
            MessageLoopThread.SetApartmentState(ApartmentState.STA);
            MessageLoopThread.IsBackground = true;
            MessageLoopThread.Start();
        }

        protected override void OnStop()
        {
            EventLog.WriteEntry(ServiceName, "Service stopping.", EventLogEntryType.Information);

            ListenerForm?.Invoke((MethodInvoker)(() =>
                {
                    ListenerForm.Close();
                }));

            MessageLoopThread?.Join(5000);
        }
    }
}
