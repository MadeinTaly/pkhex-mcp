using System;
using System.Linq;
using System.Reflection;
using PKHeX.Core;

var asm = typeof(PKM).Assembly;
string pat = args.Length > 0 ? args[0] : "Nature";

foreach (var t in asm.GetTypes().OrderBy(x => x.FullName))
{
    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy))
    {
        if (m.DeclaringType != t) continue;
        if (!m.Name.Contains(pat, StringComparison.OrdinalIgnoreCase)) continue;
        var pars = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
        Console.WriteLine($"{t.FullName}::{m.Name}({pars}) -> {m.ReturnType.Name}");
    }
}
