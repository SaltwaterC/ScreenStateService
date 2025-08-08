using System.ServiceProcess;

namespace ScreenStateService
{
    internal static class Program
    {
        static void Main(string[] _)
        {
            ServiceBase.Run(new ScreenStateService());
        }
    }
}
