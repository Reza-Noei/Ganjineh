using System;
using System.Reflection;
using NSec.Cryptography;

var asm = typeof(SignatureAlgorithm).Assembly;
var kcp = asm.GetType("NSec.Cryptography.KeyCreationParameters");
Console.WriteLine("=== KeyCreationParameters properties ===");
foreach (var p in kcp.GetProperties())
    Console.WriteLine($"  {p.Name} : {p.PropertyType.Name}");
Console.WriteLine("=== KeyExportPolicies values ===");
foreach (var v in Enum.GetValues(asm.GetType("NSec.Cryptography.KeyExportPolicies")))
    Console.WriteLine($"  {v}");
