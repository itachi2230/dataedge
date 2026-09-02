$ErrorActionPreference = 'Continue'
$dir = Resolve-Path "..\packages"
$ski = [System.Reflection.Assembly]::LoadFrom((Join-Path $dir "SkiaSharp.2.88.6\lib\net462\SkiaSharp.dll"))
function SafeTypes($asm) {
    try { return $asm.GetTypes() | Where-Object { $_ -ne $null } }
    catch [System.Reflection.ReflectionTypeLoadException] { return $_.Exception.Types | Where-Object { $_ -ne $null } }
}
function Sig($m) { try { return ($m.GetParameters() | ForEach-Object { $_.ParameterType.Name }) -join ',' } catch { return '?' } }

Write-Output "=== 6. OxyPlot ColumnItem:"
$oa = [System.Reflection.Assembly]::LoadFrom((Join-Path $dir "OxyPlot.Core.2.0.0\lib\net45\OxyPlot.dll"))
$OT = SafeTypes $oa
$ci = $OT | Where-Object { $_.Name -eq 'ColumnItem' } | Select-Object -First 1
$ci.GetConstructors() | ForEach-Object { Write-Output ("  .ctor(" + (Sig $_) + ")") }
Write-Output ("  Value prop: " + ($ci.GetProperty('Value') -ne $null))

Write-Output "=== 7. SkiaSharp text/chart essentials:"
function SkM($tn, $mn) { $t = SafeTypes $ski | Where-Object { $_.Name -eq $tn } | Select-Object -First 1; if (-not $t) { Write-Output "  MISSING $tn"; return }; $found = $t.GetMethods() | Where-Object { $_.Name -eq $mn } | Select-Object -First 2; if ($found) { foreach ($m in $found) { Write-Output ("  $tn.$mn(" + (Sig $m) + ")") } } else { Write-Output "  $tn.$mn NOT FOUND" } }
SkM 'SKData' 'SaveTo'
SkM 'SKImage' 'FromBitmap'
SkM 'SKPaint' 'MeasureText'
SkM 'SKCanvas' 'DrawLine'
SkM 'SKCanvas' 'DrawPath'
SkM 'SKPath' 'AddPoly'
$sp = (SafeTypes $ski | Where-Object { $_.Name -eq 'SKPaint' } | Select-Object -First 1)
foreach ($pn in @('Typeface','TextSize','TextAlign','IsAntialias','Color')) { Write-Output ("  SKPaint.$pn : " + ($sp.GetProperty($pn) -ne $null)) }
$fs = (SafeTypes $ski | Where-Object { $_.Name -eq 'SKFontStyle' } | Select-Object -First 1)
Write-Output ("  SKFontStyle.Bold static: " + ($fs.GetField('Bold', 'Public,Static') -ne $null))
Write-Output ("  SKEncodedImageFormat members: " + ([Enum]::GetNames((SafeTypes $ski | Where-Object { $_.Name -eq 'SKEncodedImageFormat' } | Select-Object -First 1)) -join ','))

Write-Output "=== 8. Domain model (from bin):"
$bin = [System.Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path "bin\Debug") "dataedge.dll"))
$BT = SafeTypes $bin
$tr = $BT | Where-Object { $_.Name -eq 'Trade' } | Select-Object -First 1
$tr.GetProperties() | ForEach-Object { Write-Output ("  Trade." + $_.Name + " : " + $_.PropertyType.Name) }
foreach ($en in @('Session','TypeOrdre','Resultat','TypeChamp')) { $e = $BT | Where-Object { $_.Name -eq $en -and $_.IsEnum } | Select-Object -First 1; if ($e) { Write-Output ("  enum $en : " + ([Enum]::GetNames($e) -join ',')) } }
$cp = $BT | Where-Object { $_.Name -eq 'ChampPersonnalise' } | Select-Object -First 1
if ($cp) { $cp.GetProperties() | ForEach-Object { Write-Output ("  Champ." + $_.Name + " : " + $_.PropertyType.Name) } }
$st = $BT | Where-Object { $_.FullName -match 'Strategie$' } | Select-Object -First 1
foreach ($mn in @('GetChamps','GetTrades','GetStatistics','GetAdvancedStatistics','GetStrategyName','GetDescription','GetDateDebut','GetDateFin')) { $m = $st.GetMethod($mn); if ($m) { Write-Output ("  Strategie." + $mn + "() => " + $m.ReturnType.ToString()) } else { Write-Output ("  Strategie.$mn NOT FOUND") } }
$ad = $BT | Where-Object { $_.Name -eq 'AdvancedStats' } | Select-Object -First 1
$ad.GetProperties() | ForEach-Object { Write-Output ("  AdvancedStats." + $_.Name + " : " + $_.PropertyType.ToString()) }
$ps = $BT | Where-Object { $_.Name -eq 'PerformanceStat' } | Select-Object -First 1
if ($ps) { $ps.GetProperties() | ForEach-Object { Write-Output ("  PerfStat." + $_.Name + " : " + $_.PropertyType.Name) } }
