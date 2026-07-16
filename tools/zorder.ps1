# 自顶向底 dump 可见顶层窗口 z 序（验证贴底/全屏相处用）
$sig = @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class Z {
    [DllImport("user32.dll")] public static extern IntPtr GetTopWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int l, t, r, b; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT rc);
}
'@
Add-Type -TypeDefinition $sig
$h = [Z]::GetTopWindow([IntPtr]::Zero)
$i = 0
while ($h -ne [IntPtr]::Zero -and $i -lt 400) {
    if ([Z]::IsWindowVisible($h)) {
        $cls = New-Object System.Text.StringBuilder 256; [void][Z]::GetClassName($h, $cls, 256)
        $ttl = New-Object System.Text.StringBuilder 256; [void][Z]::GetWindowText($h, $ttl, 256)
        $wpid = [uint32]0; [void][Z]::GetWindowThreadProcessId($h, [ref]$wpid)
        $pname = (Get-Process -Id $wpid -ErrorAction SilentlyContinue).ProcessName
        $rc = New-Object Z+RECT; [void][Z]::GetWindowRect($h, [ref]$rc)
        $w = $rc.r - $rc.l; $ht = $rc.b - $rc.t
        if ($w -gt 0 -and $ht -gt 0) {
            $mark = ''
            if ($ttl.ToString() -like 'WidgetProto*') { $mark = '  <<<< WIDGET' }
            "{0,3} {1,-38} {2,-24} pid={3,-6} {4} [{5},{6} {7}x{8}]{9}" -f `
                $i, $cls.ToString(), ($ttl.ToString().Substring(0, [Math]::Min(24, $ttl.Length))), `
                $wpid, $pname, $rc.l, $rc.t, $w, $ht, $mark
        }
    }
    $h = [Z]::GetWindow($h, 2)  # GW_HWNDNEXT
    $i++
}
