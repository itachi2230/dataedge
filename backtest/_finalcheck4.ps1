$ErrorActionPreference = 'Continue'
$dir = Resolve-Path "..\packages"
$oa = [System.Reflection.Assembly]::LoadFrom((Join-Path $dir "OxyPlot.Core.2.0.0\lib\net45\OxyPlot.dll"))
function SafeTypes($asm) {
    try { return $asm.GetTypes() | Where-Object { $_ -ne $null } }
    catch [System.Reflection.ReflectionTypeLoadException] { return $_.Exception.Types | Where-Object { $_ -ne $null } }
}
$exe = Join-Path (Resolve-Path "bin\Debug") "dataedge.exe"
if (-not (Test-Path $exe)) { $exe = Join-Path (Resolve-Path "bin\Debug") "backtest.exe" }
Write-Output ("Loading: " + $exe)
$bin = [System.Reflection.Assembly]::LoadFrom($exe)
$BT = SafeTypes $bin
$tr = $BT | Where-Object { $_.Name -eq 'Trade' } | Select-Object -First 1
$tr.GetProperties() | ForEach-Object { Write-Output ("  Trade." + $_.Name + " : " + $_.PropertyType.Name) }
foreach ($en in @('Session','TypeOrdre','Resultat','TypeChamp')) { $e = $BT | Where-Object { $_.Name -eq $en -and $_.IsEnum } | Select-Object -First 1; if ($e) { Write-Output ("  enum $en : " + ([Enum]::GetNames($e) -join ',')) } }
$cp = $BT | Where-Object { $_.Name -eq 'ChampPersonnalise' } | Select-Object -First 1
if ($cp) { $cp.GetProperties() | ForEach-Object { Write-Output ("  Champ." + $_.Name + " : " + $_.PropertyType.Name) } } else { Write-Output "  ChampPersonnalise MISSING (maybe named differently)" }
$st = $BT | Where-Object { $_.FullName -match 'Strategie$' } | Select-Object -First 1
Write-Output ("  Strategie type: " + $st.FullName)
foreach ($mn in @('GetChamps','GetTrades','GetStatistics','GetAdvancedStatistics','GetStrategyName','GetDescription','GetDateDebut','GetDateFin')) { $m = $st.GetMethod($mn); if ($m) { Write-Output ("  Strategie." + $mn + "() => " + $m.ReturnType.ToString()) } else { Write-Output ("  Strategie.$mn NOT FOUND") } }
$ad = $BT | Where-Object { $_.Name -eq 'AdvancedStats' } | Select-Object -First 1
$ad.GetProperties() | ForEach-Object { Write-Output ("  AdvancedStats." + $_.Name + " : " + $_.PropertyType.ToString()) }
$ps = $BT | Where-Object { $_.Name -eq 'PerformanceStat' } | Select-Object -First 1
if ($ps) { $ps.GetProperties() | ForEach-Object { Write-Output ("  PerfStat." + $_.Name + " : " + $_.PropertyType.Name) } }
$bs = $BT | Where-Object { $_.Name -eq 'CalculStatistics' -or $_.Name -eq 'Statistics' } | Select-Object -First 3
foreach ($b in $bs) { Write-Output ("  StatsType: " + $b.FullName) }
