using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;
using Microsoft.Win32;

class Program
{
    private const string serviceName = "ScreenStateService";
    private const string serviceAccount = @"NT AUTHORITY\LocalService";

    static void Main(string[] args)
    {
        bool uninstallOnly = args.Length > 0 &&
                             args[0].Equals("uninstall", StringComparison.OrdinalIgnoreCase);

        if (!IsAdministrator())
            Console.WriteLine("WARNING: Not running elevated. Service operations may fail.");

        // exePath: arg0 (unless it's 'uninstall'), else alongside helper
        string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, serviceName + ".exe");
        if (!uninstallOnly && args.Length > 0 && !args[0].Equals("uninstall", StringComparison.OrdinalIgnoreCase))
            exePath = args[0];

        if (!uninstallOnly && !File.Exists(exePath))
        {
            Console.WriteLine($"ERROR: Service executable not found at: {exePath}");
            Environment.Exit(1);
            return;
        }

        if (uninstallOnly)
        {
            UninstallServiceFlow();
            return;
        }

        bool underProgramFiles = IsUnderProgramFiles(exePath);

        if (underProgramFiles)
        {
            // MSI-owned install: do NOT create or ACL. Just ensure binPath and start.
            Console.WriteLine("Detected Program Files install. Leaving service creation/ACLs to MSI.");

            using (var sc = GetService(serviceName))
            {
                if (sc == null)
                {
                    Console.WriteLine("Service not found. Install via the MSI to create it.");
                    return;
                }

                // Ensure SCM points to the EXE we were given
                if (!BinPathMatches(serviceName, exePath))
                {
                    Console.WriteLine("Updating service binPath to match installed EXE...");
                    RunSc($@"config ""{serviceName}"" binPath= ""{exePath}""");
                }

                // Make sure account is LocalService (if MSI set it correctly, this is a no-op)
                TryRunSc($@"config ""{serviceName}"" obj= ""{serviceAccount}""");

                // Start the service
                TryStart(serviceName);
            }

            Console.WriteLine("Done.");
            return;
        }

        // Dev-loop flow (running from your repo build output)
        // Remove existing service if present
        using (var sc = GetService(serviceName))
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

                // Wait for SCM to purge the record
                for (int i = 0; i < 20; i++)
                {
                    if (GetService(serviceName) == null) break;
                    Thread.Sleep(200);
                }
            }
        }

        // Ensure LocalService can read/execute (dev folders under user profile usually need this)
        GrantReadExecuteToLocalService(exePath);

        // Create Event Log source for dev (MSI does this in real installs)
        TryCreateEventSource(serviceName);

        Console.WriteLine("Creating service...");
        RunSc($@"create ""{serviceName}"" binPath= ""{exePath}"" start= auto obj= ""{serviceAccount}""");

        // Optional niceties
        TryRunSc($@"description ""{serviceName}"" ""Monitors screen state and logs events.""");
        TryRunSc($@"sidtype ""{serviceName}"" unrestricted");
        TryRunSc($@"failure ""{serviceName}"" reset= 86400 actions= restart/60000");

        TryStart(serviceName);

        Console.WriteLine("Setup complete.");
    }

    // ----- Helpers -----

    static void TryStart(string name)
    {
        Console.WriteLine("Starting service...");
        RunSc($@"start ""{name}""");
        try
        {
            using (var sc = new ServiceController(name))
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
    }

    static void UninstallServiceFlow()
    {
        using (var sc = GetService(serviceName))
        {
            if (sc != null)
            {
                try
                {
                    if (sc.Status != ServiceControllerStatus.Stopped &&
                        sc.Status != ServiceControllerStatus.StopPending)
                    {
                        Console.WriteLine("Stopping service...");
                        sc.Stop();
                    }
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                }
                catch { /* ignore */ }

                Console.WriteLine("Deleting service...");
                TryRunSc($@"delete ""{serviceName}""");
            }
            else
            {
                Console.WriteLine("Service not found, nothing to uninstall.");
            }
        }
        TryDeleteEventSource(serviceName);
        Console.WriteLine("Uninstall complete.");
    }

    static bool IsUnderProgramFiles(string path)
    {
        string p = Path.GetFullPath(path).TrimEnd('\\');
        string pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).TrimEnd('\\');
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).TrimEnd('\\');
        return p.StartsWith(pf64, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(pf86, StringComparison.OrdinalIgnoreCase);
    }

    static bool BinPathMatches(string svcName, string desiredExe)
    {
        try
        {
            string keyPath = @"SYSTEM\CurrentControlSet\Services\" + svcName;
            using (var key = Registry.LocalMachine.OpenSubKey(keyPath, false))
            {
                if (key == null) return false;
                var imagePath = key.GetValue("ImagePath") as string;
                if (string.IsNullOrWhiteSpace(imagePath)) return false;

                imagePath = Environment.ExpandEnvironmentVariables(imagePath).Trim();
                string currentExe = ParseExeFromImagePath(imagePath);
                string desired = Path.GetFullPath(desiredExe);

                return string.Equals(currentExe, desired, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { return false; }
    }

    static string ParseExeFromImagePath(string imagePath)
    {
        imagePath = imagePath.Trim();
        if (imagePath.StartsWith("\""))
        {
            int end = imagePath.IndexOf('"', 1);
            if (end > 1) return Path.GetFullPath(imagePath.Substring(1, end - 1));
        }
        // no quotes: first token is the exe path
        int space = imagePath.IndexOf(' ');
        string exe = space > 0 ? imagePath.Substring(0, space) : imagePath;
        return Path.GetFullPath(exe);
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

            // Directory: recursive RX
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

            // EXE: explicit RX
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
            try
            {
                var dirFull = Path.GetDirectoryName(Path.GetFullPath(exePath));
                if (!string.IsNullOrEmpty(dirFull))
                    RunProcess("icacls.exe", $"\"{dirFull}\" /grant \"NT AUTHORITY\\LOCAL SERVICE\":(RX) /T /C");
            }
            catch (Exception ex2)
            {
                Console.WriteLine($"WARNING: icacls fallback failed: {ex2.Message}");
            }
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
