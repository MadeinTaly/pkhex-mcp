$a = [Reflection.Assembly]::LoadFile('C:\Users\Gabriel\.nuget\packages\pkhex.core\25.5.18\lib\net9.0\PKHeX.Core.dll')
try { $types = $a.GetTypes() }
catch [Reflection.ReflectionTypeLoadException] {
    $types = $_.Exception.Types | Where-Object { $_ -ne $null }
}
$pattern = $args[0]
$types | Where-Object { $_.Name -match $pattern } | ForEach-Object { $_.FullName }
