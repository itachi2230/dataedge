$ErrorActionPreference = 'Continue'
$dir = Resolve-Path "..\packages"
$null = [System.Reflection.Assembly]::LoadFrom((Join-Path $dir "HarfBuzzSharp.7.3.0\lib\net462\HarfBuzzSharp.dll"))
$ski = [System.Reflection.Assembly]::LoadFrom((Join-Path $dir "SkiaSharp.2.88.6\lib\net462\SkiaSharp.dll"))
$q = [System.Reflection.Assembly]::LoadFrom((Join-Path $dir "QuestPDF.2023.5.0\lib\net462\QuestPDF.dll"))
function SafeTypes($asm) {
    try { return $asm.GetTypes() | Where-Object { $_ -ne $null } }
    catch [System.Reflection.ReflectionTypeLoadException] { return $_.Exception.Types | Where-Object { $_ -ne $null } }
}
$T = SafeTypes $q
function Sig($m) { try { return ($m.GetParameters() | ForEach-Object { $_.ParameterType.Name }) -join ',' } catch { return '?' } }

Write-Output "=== 1. TextSpanDescriptor chain + style methods"
$tsd = $T | Where-Object { $_.Name -eq 'TextSpanDescriptor' } | Select-Object -First 1
$cur = $tsd
while ($cur -ne $null -and $cur.FullName -ne 'System.Object') {
    $mNames = ($cur.GetMethods() | Where-Object { $_.DeclaringType -eq $cur -and $_.Name -match 'FontSize|FontColor|FontFamily|Bold|SemiBold|Light|Medium|LineHeight|Weight|BackgroundColor|Underline|AlignLeft|AlignCenter|AlignRight' } | ForEach-Object { $_.Name + '(' + (Sig $_) + ')' }) -join ' | '
    Write-Output ("  " + $cur.FullName + " => " + $mNames)
    $cur = $cur.BaseType
}

Write-Output "=== 2. IContainer instance methods:"
$ico = $T | Where-Object { $_.Name -eq 'IContainer' } | Select-Object -First 1
$ico.GetMethods() | ForEach-Object { Write-Output ("  " + $_.Name + "(" + (Sig $_) + ")") }
Write-Output "=== 2b. IContainer ext: Element/Text/Image/Table full sigs:"
foreach ($tx in $T | Where-Object { $_.IsSealed -and $_.IsAbstract }) {
    foreach ($m in $tx.GetMethods()) {
        $pr = $null; try { $pr = $m.GetParameters() } catch { continue }
        if ($pr.Length -ge 1 -and $pr[0].ParameterType.Name -eq 'IContainer' -and $m.Name -match '^(Element|Text|Image|Table)$') {
            Write-Output ("  " + $m.Name + "(" + (($pr | Select-Object -Skip 1 | ForEach-Object { $_.ParameterType.ToString() }) -join ',') + ")")
        }
    }
}

Write-Output "=== 3. ImageDescriptor (via Image return type):"
$imgm = $null
foreach ($tx in $T | Where-Object { $_.IsSealed -and $_.IsAbstract }) { foreach ($m in $tx.GetMethods()) { $pr=$null; try { $pr = $m.GetParameters() } catch { continue }; if ($pr.Length -ge 1 -and $pr[0].ParameterType.Name -eq 'IContainer' -and $m.Name -eq 'Image') { $imgm = $m; break } }; if ($imgm) { break } }
$idt = $imgm.ReturnType
Write-Output ("  return type: " + $idt.FullName)
$idt.GetMethods() | Where-Object { $_.DeclaringType -eq $idt -or $_.DeclaringType.Name -match 'Extensions' } | ForEach-Object { Write-Output ("  " + $_.Name + "(" + (Sig $_) + ")") }

Write-Output "=== 4. Table descriptors:"
$td2 = $T | Where-Object { $_.Name -eq 'TableDescriptor' } | Select-Object -First 1
$td2.GetMethods() | Where-Object { $_.DeclaringType -eq $td2 } | ForEach-Object { Write-Output ("  TableDescriptor." + $_.Name + "(" + (Sig $_) + ") => " + $_.ReturnType.Name) }
$hd = $T | Where-Object { $_.Name -match '^HeaderDescriptor$' } | Select-Object -First 1
if ($hd) { $hd.GetMethods() | Where-Object { $_.DeclaringType -eq $hd } | ForEach-Object { Write-Output ("  HeaderDescriptor." + $_.Name + "(" + (Sig $_) + ") => " + $_.ReturnType.Name) } }
$cd2 = $T | Where-Object { $_.Name -eq 'ColumnsDescriptor' -or $_.Name -eq 'ColumnsDefinition' } | Select-Object -First 1
if ($cd2) { $cd2.GetMethods() | Where-Object { $_.DeclaringType -eq $cd2 } | ForEach-Object { Write-Output ("  " + $cd2.Name + "." + $_.Name + "(" + (Sig $_) + ")") } }
$tcd = $T | Where-Object { $_.Name -match 'TableCellDescriptor' } | Select-Object -First 1
if ($tcd) { $tcd.GetMethods() | Where-Object { $_.DeclaringType -eq $tcd } | ForEach-Object { Write-Output ("  TableCell." + $_.Name + "(" + (Sig $_) + ")") } }

Write-Output "=== 5. Grid descriptors:"
$gd = $T | Where-Object { $_.Name -eq 'GridDescriptor' } | Select-Object -First 1
$gd.GetMethods() | Where-Object { $_.DeclaringType -eq $gd } | ForEach-Object { Write-Output ("  GridDescriptor." + $_.Name + "(" + (Sig $_) + ") => " + $_.ReturnType.Name) }
$grd = $T | Where-Object { $_.Name -eq 'GridRowDescriptor' } | Select-Object -First 1
if ($grd) { $grd.GetMethods() | Where-Object { $_.DeclaringType -eq $grd } | ForEach-Object { Write-Output ("  GridRow." + $_.Name + "(" + (Sig $_) + ") => " + $_.ReturnType.Name) } }
