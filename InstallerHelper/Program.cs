using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;

class Program
{
    private const string serviceName = "ScreenStateService";
    private const string serviceAccount = @"NT AUTHORITY\LocalService"; // explicit

    static void Main(string[] args)
    {
        bool uninstallOnly = args.Length > 0 &&
                             args[0].Equals("uninstall", StringComparison.OrdinalIgnoreCase);

        if (!IsAdministrator())
            Console.WriteLine("WARNING: Not running elevated. Service install/delete may fail.");

        // exePath: arg0 (if not 'uninstall') or alongside helper
        string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, serviceName + ".exe");
        if (!uninstallOnly)
        {
            if (args.Length > 0 && !args[0].Equals("uninstall", StringComparison.OrdinalIgnoreCase))
                exePath = args[0];

            if (!File.Exists(exePath))
            {
                Console.WriteLine($"ERROR: Service executable not found at: {exePath}");
                Environment.Exit(1);
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
                    if (sc.Status != ServiceControllerStatus.Stopped &&
                        sc.Status != ServiceControllerStatus.StopPending)
                    {
                        Console.WriteLine("Stopping existing service...");
                        sc.Stop();
                    }
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                }
                catch { /* ignore */ }

                TryDeleteEventSource(serviceName);
                Console.WriteLine("Deleting existing service...");
                RunSc($@"delete ""{serviceName}""");

                // Wait for SCM to actually drop it
                for (int i = 0; i < 20; i++)
                {
                    if (GetService(serviceName) == null) break;
                    Thread.Sleep(200);
                }
            }
            else if (uninstallOnly)
            {
                Console.WriteLine("Service not found, nothing to uninstall.");
                Console.WriteLine("Uninstall complete.");
                return;
            }
        }

        if (uninstallOnly)
        {
            TryDeleteEventSource(serviceName);
            Console.WriteLine("Uninstall complete.");
            return;
        }

        // Ensure LocalService can read/execute the EXE and everything under its folder
        GrantReadExecuteToLocalService(exePath);

        // Precreate Event Log source (needs admin)
        TryCreateEventSource(serviceName);

        Console.WriteLine("Creating service...");
        // sc.exe requires spaces after '='
        RunSc($@"create ""{serviceName}"" binPath= ""{exePath}"" start= auto obj= ""{serviceAccount}""");

        // Optional niceties
        TryRunSc($@"description ""{serviceName}"" ""Monitors screen state and logs events.""");
        TryRunSc($@"sidtype ""{serviceName}"" unrestricted");
        TryRunSc($@"failure ""{serviceName}"" reset= 86400 actions= restart/60000");

        Console.WriteLine("Starting service...");
        RunSc($@"start ""{serviceName}""");

        try
        {
            using (var sc = new ServiceController(serviceName))
            {
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                Console.WriteLine(sc.Status == ServiceControllerStatus.Running
                    ? "Service is RUNNING."
                    : $"Service status is: {sc.Status}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking service status: {ex.Message}");
        }

        Console.WriteLine("Setup complete.");
    }

    static void GrantReadExecuteToLocalService(string exePath)
    {
        try
        {
            var exeFull = Path.GetFullPath(exePath);
            var dirFull = Path.GetDirectoryName(exeFull);
            if (string.IsNullOrEmpty(dirFull) || !Directory.Exists(dirFull))
            {
                Console.WriteLine("WARNING: Could not resolve directory for ACL grant.");
                return;
            }

            Console.WriteLine($@"Granting RX to LocalService on: {dirFull}");
            var localServiceSid = new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null);
            var localServiceAccount = (NTAccount)localServiceSid.Translate(typeof(NTAccount));

            // Directory: grant RX recursively
            var dsec = Directory.GetAccessControl(dirFull);
            var dirRule = new FileSystemAccessRule(
                localServiceAccount,
                FileSystemRights.ReadAndExecute | FileSystemRights.ListDirectory | FileSystemRights.Read,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow);
            bool modified;
            dsec.ModifyAccessRule(AccessControlModification.Add, dirRule, out modified);
            Directory.SetAccessControl(dirFull, dsec);

            // EXE file: explicit RX (covers cases where parent ACL inheritance is blocked)
            if (File.Exists(exeFull))
            {
                var fsec = File.GetAccessControl(exeFull);
                var fileRule = new FileSystemAccessRule(
                    localServiceAccount,
                    FileSystemRights.ReadAndExecute | FileSystemRights.Read,
                    AccessControlType.Allow);
                fsec.ModifyAccessRule(AccessControlModification.Add, fileRule, out modified);
                File.SetAccessControl(exeFull, fsec);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: .NET ACL grant failed ({ex.Message}). Trying icacls...");
            // Fallback: icacls (grant RX recursively)
            try
            {
                var dirFull = Path.GetDirectoryName(Path.GetFullPath(exePath));
                if (!string.IsNullOrEmpty(dirFull))
                {
                    RunProcess("icacls.exe", $"\"{dirFull}\" /grant \"NT AUTHORITY\\LOCAL SERVICE\":(RX) /T /C");
                }
            }
            catch (Exception ex2)
            {
                Console.WriteLine($"WARNING: icacls fallback failed: {ex2.Message}");
            }
        }
    }

    static void RunProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using (var p = Process.Start(psi))
        {
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (!string.IsNullOrWhiteSpace(stdout)) Console.WriteLine(stdout.Trim());
            if (!string.IsNullOrWhiteSpace(stderr)) Console.WriteLine(stderr.Trim());
            if (p.ExitCode != 0)
                throw new Exception($"{fileName} failed ({p.ExitCode})");
        }
    }

    static bool IsAdministrator()
    {
        try
        {
            using (var id = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(id);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
        catch { return false; }
    }

    static ServiceController GetService(string name)
    {
        foreach (var sc in ServiceController.GetServices())
            if (string.Equals(sc.ServiceName, name, StringComparison.OrdinalIgnoreCase))
                return sc;
        return null;
    }

    static void RunSc(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using (var proc = Process.Start(psi))
        {
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            if (!string.IsNullOrWhiteSpace(stdout)) Console.WriteLine(stdout.Trim());
            if (!string.IsNullOrWhiteSpace(stderr)) Console.WriteLine(stderr.Trim());

            if (proc.ExitCode != 0)
                throw new Exception($@"sc.exe failed ({proc.ExitCode}) for: sc {arguments}");
        }
    }

    static void TryRunSc(string arguments)
    {
        try { RunSc(arguments); }
        catch (Exception ex) { Console.WriteLine($"(Non-fatal) sc {arguments} -> {ex.Message}"); }
    }

    static void TryCreateEventSource(string source, string logName = "Application")
    {
        try
        {
            if (!EventLog.SourceExists(source))
            {
                Console.WriteLine($@"Creating Event Log source ""{source}""...");
                EventLog.CreateEventSource(source, logName);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: Could not create Event Log source: {ex.Message}");
        }
    }

    static void TryDeleteEventSource(string source)
    {
        try { if (EventLog.SourceExists(source)) EventLog.DeleteEventSource(source); }
        catch (Exception ex) { Console.WriteLine($"WARNING: Could not delete Event Log source: {ex.Message}"); }
    }
}
