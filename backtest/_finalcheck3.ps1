$ErrorActionPreference = 'Continue'
$dir = Resolve-Path "..\packages"
$null = [System.Reflection.Assembly]::LoadFrom((Join-Path $dir "HarfBuzzSharp.7.3.0\lib\net462\HarfBuzzSharp.dll"))
$null = [System.Reflection.Assembly]::LoadFrom((Join-Path $dir "SkiaSharp.2.88.6\lib\net462\SkiaSharp.dll"))
$q = [System.Reflection.Assembly]::LoadFrom((Join-Path $dir "QuestPDF.2023.5.0\lib\net462\QuestPDF.dll"))
function SafeTypes($asm) {
    try { return $asm.GetTypes() | Where-Object { $_ -ne $null } }
    catch [System.Reflection.ReflectionTypeLoadException] { return $_.Exception.Types | Where-Object { $_ -ne $null } }
}
$T = SafeTypes $q
function Sig($m) { try { return ($m.GetParameters() | ForEach-Object { $_.ParameterType.ToString() }) -join ',' } catch { return '?' } }

Write-Output "=== A. TextSpanDescriptor interfaces + style extensions:"
$tsd = $T | Where-Object { $_.Name -eq 'TextSpanDescriptor' } | Select-Object -First 1
$ifaces = $tsd.GetInterfaces() | ForEach-Object { $_.Name }
Write-Output ("  interfaces: " + ($ifaces -join ', '))
foreach ($tx in $T | Where-Object { $_.IsSealed -and $_.IsAbstract }) {
    foreach ($m in $tx.GetMethods()) {
        $pr = $null; try { $pr = $m.GetParameters() } catch { continue }
        if ($pr.Length -ge 1 -and ($ifaces -contains $pr[0].ParameterType.Name)) {
            Write-Output ("  " + $m.Name + "(" + (($pr | Select-Object -Skip 1 | ForEach-Object { $_.ParameterType.ToString() }) -join ',') + ") on " + $pr[0].ParameterType.Name)
        }
    }
}
$col = $T | Where-Object { $_.FullName -eq 'QuestPDF.Helpers.Colors' } | Select-Object -First 1
if ($col) {
    Write-Output ("  Colors class EXISTS; sample statics: " + ((($col.GetFields('Public,Static') | Where-Object { $_.Name -in @('Red','Green','Blue','White','Black','Amber','Grey','Teal') } | ForEach-Object { $_.Name }) -join ',')))
} else { Write-Output "  Colors class MISSING" }

Write-Output "=== B. Table delegate types + cell container:"
$td2 = $T | Where-Object { $_.Name -eq 'TableDescriptor' } | Select-Object -First 1
$h = $td2.GetMethod('Header'); Write-Output ("  Header arg: " + $h.GetParameters()[0].ParameterType.GetGenericArguments()[0].FullName)
$c = $td2.GetMethod('ColumnsDefinition'); Write-Output ("  ColumnsDefinition arg: " + $c.GetParameters()[0].ParameterType.GetGenericArguments()[0].FullName)
$cellT = $T | Where-Object { $_.Name -eq 'ITableCellContainer' } | Select-Object -First 1
Write-Output ("  ITableCellContainer interfaces: " + (($cellT.GetInterfaces() | ForEach-Object { $_.Name }) -join ', '))
foreach ($tx in $T | Where-Object { $_.IsSealed -and $_.IsAbstract }) {
    foreach ($m in $tx.GetMethods()) {
        $pr = $null; try { $pr = $m.GetParameters() } catch { continue }
        if ($pr.Length -ge 1 -and $pr[0].ParameterType.Name -eq 'ITableCellContainer') {
            Write-Output ("  cell." + $m.Name + "(" + (($pr | Select-Object -Skip 1 | ForEach-Object { $_.ParameterType.ToString() }) -join ',') + ")")
        }
    }
}
