using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;

class Program
{
    static void Main(string[] args)
    {
        const string serviceName = "ScreenStateService";

        // Check if uninstall flag is present
        bool uninstallOnly = args.Length > 0 && args[0].Equals("uninstall", StringComparison.OrdinalIgnoreCase);

        // If uninstall only, exePath is irrelevant and no need to check it
        string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, serviceName + ".exe");
        if (!uninstallOnly)
        {
            exePath = args.Length > 0 ? args[0] : exePath;

            if (!File.Exists(exePath))
            {
                Console.WriteLine($"ERROR: Service executable not found at: {exePath}");
                return;
            }
        }

        // Remove existing service if present
        using (ServiceController sc = GetService(serviceName))
        {
            if (sc != null)
            {
                try
                {
                    if (sc.Status != ServiceControllerStatus.Stopped)
                    {
                        Console.WriteLine("Stopping existing service...");
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    }
                }
                catch { /* ignore */ }

                Console.WriteLine("Deleting existing service...");
                RunSc($"delete \"{serviceName}\"");
            }
            else if (uninstallOnly)
            {
                Console.WriteLine("Service not found, nothing to uninstall.");
                return;
            }
        }

        if (uninstallOnly)
        {
            Console.WriteLine("Uninstall complete.");
            return;
        }

        // Install service to run as LocalService, auto-start
        Console.WriteLine("Creating service...");
        RunSc($"create \"{serviceName}\" binPath= \"{exePath}\" start= auto");

        // Start service immediately
        Console.WriteLine("Starting service...");
        RunSc($"start \"{serviceName}\"");

        // Check status
        try
        {
            using (var sc = new ServiceController(serviceName))
            {
                sc.Refresh();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                if (sc.Status == ServiceControllerStatus.Running)
                {
                    Console.WriteLine("Service is RUNNING.");
                }
                else
                {
                    Console.WriteLine($"Service status is: {sc.Status}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking service status: {ex.Message}");
        }

        Console.WriteLine("Setup complete.");
    }

    static ServiceController GetService(string name)
    {
        foreach (var sc in ServiceController.GetServices())
        {
            if (string.Equals(sc.ServiceName, name, StringComparison.OrdinalIgnoreCase))
                return sc;
        }
        return null;
    }

    static void RunSc(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var proc = Process.Start(psi);
        proc.WaitForExit();
    }
}
