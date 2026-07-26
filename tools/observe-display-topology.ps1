[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# 只读观测器：用于接入/拔出第二物理屏或虚拟显示驱动后，记录 Windows 真正激活的桌面拓扑。
# Win32_DesktopMonitor 是遗留 WMI，遇到同 EDID 的双输入会漏报；这里改用和 MacWidget 相同的
# EnumDisplayMonitors / GetMonitorInfo 路径。PnP 列表仅作驱动与历史 EDID 排障用途。
if (-not ('DisplayTopologyProbe' -as [type])) {
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public sealed class DisplayTopologyProbeInfo
{
    public string Device { get; set; }
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int WorkLeft { get; set; }
    public int WorkTop { get; set; }
    public int WorkWidth { get; set; }
    public int WorkHeight { get; set; }
    public uint Dpi { get; set; }
    public bool Primary { get; set; }
}

public static class DisplayTopologyProbe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    public delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFOEX info);

    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);

    [DllImport("user32.dll")]
    static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);

    public static DisplayTopologyProbeInfo[] GetAll()
    {
        var list = new List<DisplayTopologyProbeInfo>();
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4)); // PER_MONITOR_AWARE_V2，避免 PowerShell 的 DPI 虚拟化
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data)
            {
                var info = new MONITORINFOEX();
                info.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (!GetMonitorInfoW(monitor, ref info)) return true;
                uint dpiX = 96, dpiY = 96;
                GetDpiForMonitor(monitor, 0, out dpiX, out dpiY);
                list.Add(new DisplayTopologyProbeInfo
                {
                    Device = info.szDevice,
                    Left = info.rcMonitor.Left, Top = info.rcMonitor.Top,
                    Width = info.rcMonitor.Right - info.rcMonitor.Left,
                    Height = info.rcMonitor.Bottom - info.rcMonitor.Top,
                    WorkLeft = info.rcWork.Left, WorkTop = info.rcWork.Top,
                    WorkWidth = info.rcWork.Right - info.rcWork.Left,
                    WorkHeight = info.rcWork.Bottom - info.rcWork.Top,
                    Dpi = dpiX, Primary = (info.dwFlags & 1) != 0,
                });
                return true;
            }, IntPtr.Zero);
        }
        finally
        {
            if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous);
        }
        return list.ToArray();
    }
}
'@
}

$personalize = Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
$active = @([DisplayTopologyProbe]::GetAll())

$pnp = @(Get-PnpDevice -Class Monitor | ForEach-Object {
    [pscustomobject]@{
        Name = $_.FriendlyName
        InstanceId = $_.InstanceId
        Status = $_.Status
    }
})

$drivers = @(Get-CimInstance Win32_PnPSignedDriver |
    Where-Object { $_.DeviceClass -in @('DISPLAY', 'Monitor') } |
    ForEach-Object {
        [pscustomobject]@{
            Class = $_.DeviceClass
            Device = $_.DeviceName
            Provider = $_.DriverProviderName
            Version = $_.DriverVersion
        }
    })

$log = Join-Path $env:LOCALAPPDATA 'MacWidget\macwidget.log'
$lastTopology = if (Test-Path -LiteralPath $log) {
    @(Get-Content -LiteralPath $log -Tail 3000 |
        Where-Object { $_ -like '*display topology stable:*' } |
        Select-Object -Last 1)[0]
}

[pscustomobject]@{
    CapturedAt = Get-Date -Format 'o'
    ActiveDisplayCount = $active.Count
    ActiveDesktopMonitors = $active
    KnownPnpMonitors = $pnp
    DisplayDrivers = $drivers
    EnableTransparency = [int]$personalize.EnableTransparency
    AppsUseLightTheme = [int]$personalize.AppsUseLightTheme
    MacWidgetTopologyLog = [string]$lastTopology
}
