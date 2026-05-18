$a = [Reflection.Assembly]::LoadFile('C:\Users\Gabriel\.nuget\packages\pkhex.core\25.5.18\lib\net9.0\PKHeX.Core.dll')
try { $types = $a.GetTypes() }
catch [Reflection.ReflectionTypeLoadException] {
    $types = $_.Exception.Types | Where-Object { $_ -ne $null }
}
$pattern = $args[0]
foreach ($t in $types) {
    if ($t -eq $null) { continue }
    try {
        $methods = $t.GetMethods([Reflection.BindingFlags] 'Public,Static,Instance,FlattenHierarchy')
    } catch { continue }
    foreach ($m in $methods) {
        if ($m.Name -match $pattern) {
            $params = ($m.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ', '
            Write-Output ("{0}::{1}({2}) -> {3}" -f $t.FullName, $m.Name, $params, $m.ReturnType.Name)
        }
    }
}
