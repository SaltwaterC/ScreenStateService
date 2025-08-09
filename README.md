# ScreenStateService

Windows (7+) service useful for detecting screen events (off/on/dimmed) and log them to Event Viewer. By itself this service doesn't do a lot which is by design. It is supposed to be an event emitter for other services/applications/scheduled tasks.

Unfortunately, this is a power event that it is not available out of the box for use by other pieces of software, so this is the very narrow scope this service fulfils.

Under the bonnet, this creates a hidden window to [monitor GUID_CONSOLE_DISPLAY_STATE](https://learn.microsoft.com/en-us/windows/win32/power/power-setting-guids) and expose the Data member to Event Viewer. To ensure that this functions correctly as a service, the Windows Forms application loop (`Application.Run()`) is spawned inside a separate thread.

```
Windows 7, Windows Server 2008 R2, Windows Vista and Windows Server 2008: This notification
is available starting with Windows 8 and Windows Server 2012.

The Data member is a DWORD with a value from the MONITOR_DISPLAY_STATE enumeration:

PowerMonitorOff (0) - The display is off.
PowerMonitorOn (1) - The display is on.
PowerMonitorDim (2) - The display is dimmed.
```

It logs to the `Application` log using `ScreenStateService` source and with the following Event IDs:

- `0` - used by the service logger by default (start/stop messages)
- `999` - Unknown screen state. Normally you shouldn't see this as the Data member is clearly defined.
- `1000` - Screen turned off.
- `1001` - Screen turned on.
- `1002` - Screen dimmed.

The Event IDs that you are interested in are basically MONITOR_DISPLAY_STATE + 1000.

## Install

Download release and install e.g. ScreenStateService-x64-1.0.0.msi
