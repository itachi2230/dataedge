$ErrorActionPreference = 'Stop'
$dir = Resolve-Path "..\packages"
$null = [System.Reflection.Assembly]::LoadFrom((Join-Path $dir "HarfBuzzSharp.7.3.0\lib\net462\HarfBuzzSharp.dll"))
$null = [System.Reflection.Assembly]::LoadFrom((Join-Path $dir "SkiaSharp.2.88.6\lib\net462\SkiaSharp.dll"))
$q = [System.Reflection.Assembly]::LoadFrom((Join-Path $dir "QuestPDF.2023.5.0\lib\net462\QuestPDF.dll"))
function SafeTypes($asm) {
    try { return $asm.GetTypes() | Where-Object { $_ -ne $null } }
    catch [System.Reflection.ReflectionTypeLoadException] { return $_.Exception.Types | Where-Object { $_ -ne $null } }
}
function SafeSig($m) {
    try { return ($m.GetParameters() | ForEach-Object { $_.ParameterType.Name }) -join ',' } catch { return '?' }
}
$T = SafeTypes $q
foreach ($n in @('ColumnDescriptor','RowDescriptor','GridDescriptor','ImageDescriptor','TableColumnsDefinition','TableCellContainer')) {
    $x = $T | Where-Object { $_.Name -eq $n } | Select-Object -First 1
    if ($x) {
        Write-Output ("=== " + $n + " (" + $x.FullName + ") :")
        $ms = $x.GetMethods() | Where-Object { $_.DeclaringType -eq $x -and -not $_.IsSpecialName }
        foreach ($m in $ms) { Write-Output ("  " + $m.Name + "(" + (SafeSig $m) + ") => " + $m.ReturnType.Name) }
        $props = $x.GetProperties() | ForEach-Object { $_.Name }
        if ($props) { Write-Output ("  PROPS: " + ($props -join ', ')) }
    } else { Write-Output "=== $n MISSING" }
}
