$ErrorActionPreference = 'Stop'
$dir = Resolve-Path "..\packages"
$null = [System.Reflection.Assembly]::LoadFrom((Join-Path $dir "HarfBuzzSharp.7.3.0\lib\net462\HarfBuzzSharp.dll"))
$ski = [System.Reflection.Assembly]::LoadFrom((Join-Path $dir "SkiaSharp.2.88.6\lib\net462\SkiaSharp.dll"))
$q = [System.Reflection.Assembly]::LoadFrom((Join-Path $dir "QuestPDF.2023.5.0\lib\net462\QuestPDF.dll"))

function SafeTypes($asm) {
    try { return $asm.GetTypes() | Where-Object { $_ -ne $null } }
    catch [System.Reflection.ReflectionTypeLoadException] { return $_.Exception.Types | Where-Object { $_ -ne $null } }
}
function SafeSig($m) {
    try { return ($m.GetParameters() | ForEach-Object { $_.ParameterType.Name }) -join ',' } catch { return '?' }
}
$S = SafeTypes $ski
$T = SafeTypes $q

$ss = $S | Where-Object { $_.Name -eq 'SKSurface' } | Select-Object -First 1
Write-Output "--- SKSurface static Create:"
foreach ($m in $ss.GetMethods() | Where-Object { $_.Name -eq 'Create' -and $_.IsStatic }) {
    Write-Output ("  Create(" + (SafeSig $m) + ") => " + $m.ReturnType.Name)
}
$p = $S | Where-Object { $_.Name -eq 'SKPaint' } | Select-Object -First 1
Write-Output ("--- SKPaint MeasureText names: " + (($p.GetMethods() | Where-Object { $_.Name -eq 'MeasureText' } | ForEach-Object { $_.Name }) -join ', '))
$canv = $S | Where-Object { $_.Name -eq 'SKCanvas' } | Select-Object -First 1
Write-Output ("--- SKCanvas draw methods: " + (($canv.GetMethods() | Where-Object { $_.Name -match '^(DrawPath|DrawText|DrawRoundRect|DrawCircle|DrawLine|Clear|DrawRect|Save|Restore|ClipRect)$' } | ForEach-Object { $_.Name } | Sort-Object -Unique) -join ', '))
$ty = $S | Where-Object { $_.Name -eq 'SKTypeface' } | Select-Object -First 1
Write-Output ("--- SKTypeface static: " + (($ty.GetMethods() | Where-Object { $_.IsStatic -and $_.Name -match 'FromFamilyName|FromData|Default' } | ForEach-Object { $_.Name }) -join ', '))

$pe = $T | Where-Object { $_.IsSealed -and $_.IsAbstract }
Write-Output "--- PageSize ext methods:"
foreach ($tx in $pe) { foreach ($m in $tx.GetMethods()) { $pr = $null; try { $pr = $m.GetParameters() } catch { continue }; if ($pr.Length -ge 1 -and $pr[0].ParameterType.Name -eq 'PageSize') { Write-Output ("  " + $m.Name + "(" + (($pr | ForEach-Object { $_.ParameterType.Name }) -join ',') + ")") } } }
Write-Output "--- ImageDescriptor ext methods:"
foreach ($tx in $pe) { foreach ($m in $tx.GetMethods()) { $pr = $null; try { $pr = $m.GetParameters() } catch { continue }; if ($pr.Length -ge 1 -and $pr[0].ParameterType.Name -eq 'ImageDescriptor') { Write-Output ("  " + $m.Name + "(" + (($pr | Select-Object -Skip 1 | ForEach-Object { $_.ParameterType.Name }) -join ',') + ")") } } }
foreach ($n in @('GridDescriptor','TableDescriptor','ColumnsDescriptor','CellDescriptor')) {
    $x = $T | Where-Object { $_.Name -eq $n } | Select-Object -First 1
    if ($x) { Write-Output ("--- " + $n + ": " + (($x.GetMethods() | Where-Object { $_.DeclaringType -eq $x } | ForEach-Object { $_.Name + '(' + (SafeSig $_) + ')' } | Sort-Object -Unique) -join ', ')) }
    else { Write-Output "--- $n MISSING" }
}
