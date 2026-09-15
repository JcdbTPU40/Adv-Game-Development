<#
.SYNOPSIS
  #65 展示ビルドの見張り。ゲームが固まったり落ちたりしたら起動しなおして、タイトルへもどす（企画書 v8 17章 T7「故障しても60秒以内に戻す」）

.DESCRIPTION
  ・ゲームを -toufukuHeartbeat <ファイル> 付きで起動する。ゲームはメインスレッドが動いている間、1秒ごとにこのファイルを書く（ExhibitHeartbeat.cs）
  ・ファイルが StaleSeconds 秒更新されない（フリーズ）、プロセスが終わった（クラッシュ・まちがって閉じた）、
    起動してから StartupGraceSeconds 秒たっても1回も書かれない（起動で固まった）、のどれかでプロセスを止めて起動しなおす
  ・止まった時刻（最後にファイルが書かれた時刻）から、起動しなおしたゲームがまたファイルを書くまでの秒を「復帰秒」として CSV に残す（T7 の記録）
  ・見張りをやめるときは、このウィンドウで Ctrl+C（ゲームは閉じない）
  ・起動しなおしたゲームは Build Settings の 0 番のシーンから始まる。0 番をタイトルにしておくこと

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tools\exhibit_watchdog.ps1 -ExePath "D:\Build\Toufuku\Toufuku.exe"

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tools\exhibit_watchdog.ps1 -ExePath "D:\Build\Toufuku\Toufuku.exe" -StaleSeconds 8 -ExtraArguments "-screen-fullscreen 1"
#>
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [string]$HeartbeatPath = (Join-Path $env:TEMP 'toufuku_heartbeat.txt'),
    [int]$StaleSeconds = 10,
    [int]$StartupGraceSeconds = 45,
    [string]$LogPath = '',
    [string]$ExtraArguments = '',
    [int]$PollMilliseconds = 500
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ExePath)) { throw "ExePath が見つかりません: $ExePath" }
if ([string]::IsNullOrEmpty($LogPath)) { $LogPath = Join-Path (Split-Path -Parent $ExePath) 'exhibit_watchdog_log.csv' }

function Write-WatchLog([string]$EventName, [string]$Detail, [string]$Seconds = '') {
    if (-not (Test-Path -LiteralPath $LogPath)) {
        Set-Content -LiteralPath $LogPath -Value 'time,event,seconds,detail' -Encoding UTF8
    }
    $line = '{0},{1},{2},{3}' -f (Get-Date -Format 'yyyy-MM-ddTHH:mm:ss.fff'), $EventName, $Seconds, ($Detail -replace ',', ';')
    Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8
    Write-Host $line
}

# 生存確認ファイルの更新時刻（ローカル時刻）。なければ $null
function Get-HeartbeatTime {
    if (-not (Test-Path -LiteralPath $HeartbeatPath)) { return $null }
    return (Get-Item -LiteralPath $HeartbeatPath).LastWriteTime
}

function Start-Game {
    $arguments = ('-toufukuHeartbeat "{0}" {1}' -f $HeartbeatPath, $ExtraArguments).Trim()
    $p = Start-Process -FilePath $ExePath -ArgumentList $arguments -PassThru
    Write-WatchLog 'start' ('pid={0} args={1}' -f $p.Id, $arguments)
    return $p
}

Write-WatchLog 'watchdog_begin' ('exe={0} heartbeat={1} stale={2}s grace={3}s' -f $ExePath, $HeartbeatPath, $StaleSeconds, $StartupGraceSeconds)

$process = Start-Game
$startedAt = Get-Date
$pendingReason = $null   # 起動しなおして、復帰を待っている理由
$failedAt = $null        # 止まったとみなす時刻

while ($true) {
    Start-Sleep -Milliseconds $PollMilliseconds

    $now = Get-Date
    $beat = Get-HeartbeatTime
    # 今回起動したあとに書かれたか
    $beatFresh = ($null -ne $beat) -and ($beat -gt $startedAt)

    if ($pendingReason -and $beatFresh) {
        Write-WatchLog 'recovered' $pendingReason ('{0:0.0}' -f ($beat - $failedAt).TotalSeconds)
        $pendingReason = $null
    }

    $reason = $null
    $lastAlive = $now
    if ($process.HasExited) {
        $reason = 'exited'
        if ($beatFresh) { $lastAlive = $beat }
    }
    elseif ($beatFresh) {
        $age = ($now - $beat).TotalSeconds
        if ($age -ge $StaleSeconds) {
            $reason = ('frozen heartbeat_age={0:0.0}s' -f $age)
            $lastAlive = $beat
        }
    }
    elseif (($now - $startedAt).TotalSeconds -ge $StartupGraceSeconds) {
        $reason = ('no_heartbeat_within_{0}s' -f $StartupGraceSeconds)
        $lastAlive = $startedAt
    }

    if ($reason) {
        # 復帰を待っている間にまた止まったときは、最初に止まった時刻から数えつづける
        if (-not $pendingReason) { $failedAt = $lastAlive }
        Write-WatchLog 'failure' $reason
        if (-not $process.HasExited) {
            try { Stop-Process -Id $process.Id -Force } catch { Write-WatchLog 'kill_failed' $_.Exception.Message }
            [void]$process.WaitForExit(5000)
        }
        $process = Start-Game
        $startedAt = Get-Date
        if (-not $pendingReason) { $pendingReason = $reason }
    }
}
