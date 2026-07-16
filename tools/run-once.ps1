# 跑一轮：清场 -> 启动 N 个组件 -> 静置 -> 采样若干次 -> （默认）清场
param(
    [int]$N = 1,
    [string]$Control = 'hwnd',    # hwnd | comp | native
    [string]$Backdrop = 'acrylic',# none | mica | acrylic | tabbed
    [string]$Origin = 'same',     # same | multi
    [string]$Pin = 'bottom',      # bottom | none
    [string]$Widget = 'mixed',
    [string]$Glass = 'extend',
    [int]$SettleSec = 40,
    [int]$Samples = 3,
    [int]$IntervalSec = 10,
    [switch]$KeepAlive
)
$root = 'C:\work\widgetproto'

Stop-Process -Name WidgetProto -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" |
    Where-Object { $_.CommandLine -like '*widgetproto*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

Start-Process -FilePath "$root\app\WidgetProto.exe" `
    -ArgumentList "--n $N --control $Control --backdrop $Backdrop --origin $Origin --pin $Pin --widget $Widget --glass $Glass" `
    -WorkingDirectory "$root\app"
Start-Sleep -Seconds $SettleSec

"### N=$N control=$Control backdrop=$Backdrop origin=$Origin pin=$Pin widget=$Widget"
for ($i = 0; $i -lt $Samples; $i++) {
    if ($i) { Start-Sleep -Seconds $IntervalSec }
    & "$root\tools\measure-mem.ps1"
}

if (-not $KeepAlive) {
    Stop-Process -Name WidgetProto -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}
