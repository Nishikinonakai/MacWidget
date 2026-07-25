# 采样 MacWidget 全进程树内存（host + 匹配本产品 udf 的 msedgewebview2 全家）
# 输出：总计行 + 按 WS 降序的每进程明细。WS=工作集，Priv=私有提交。
$procs = Get-CimInstance Win32_Process | Where-Object {
    $_.Name -eq 'MacWidget.exe' -or $_.Name -eq 'WidgetProto.exe' -or
    ($_.Name -eq 'msedgewebview2.exe' -and $_.CommandLine -like '*widgetproto*')
}
$rows = @()
foreach ($p in $procs) {
    $gp = Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue
    if (-not $gp) { continue }
    $type = 'host'
    if ($p.Name -eq 'msedgewebview2.exe') {
        $type = 'browser'
        if ($p.CommandLine -match '--type=([a-z\-]+)') { $type = $Matches[1] }
        if ($p.CommandLine -match '--utility-sub-type=(\S+)') {
            $type = 'utility:' + ($Matches[1] -split '\.')[-1]
        }
    }
    $rows += [pscustomobject]@{
        Pid  = $p.ProcessId
        Type = $type
        WS   = [math]::Round($gp.WorkingSet64 / 1MB, 1)
        Priv = [math]::Round($gp.PrivateMemorySize64 / 1MB, 1)
    }
}
if ($rows.Count -eq 0) { "NO PROCESSES"; exit 0 }
$ws = [math]::Round(($rows | Measure-Object WS -Sum).Sum, 1)
$pv = [math]::Round(($rows | Measure-Object Priv -Sum).Sum, 1)
"{0} TOTAL procs={1} WS={2}MB Priv={3}MB" -f (Get-Date -Format 'HH:mm:ss'), $rows.Count, $ws, $pv
$rows | Sort-Object WS -Descending | ForEach-Object {
    "  pid={0,-6} {1,-22} WS={2,7}MB Priv={3,7}MB" -f $_.Pid, $_.Type, $_.WS, $_.Priv
}
