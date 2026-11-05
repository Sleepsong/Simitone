using System;
using System.Reflection;
using System.Runtime.InteropServices;

// Reads "Type size N" / "Type.field N" lines (from layout.c) on stdin and checks the C# structs match.
static class Program
{
    static int Main()
    {
        var asm = typeof(Simitone.VR.OpenXR.XrPosef).Assembly;
        int checkedCount = 0, failures = 0;
        string line;
        while ((line = Console.ReadLine()) != null)
        {
            var parts = line.Split(' ');
            if (parts.Length < 2) continue;
            var expected = long.Parse(parts[parts.Length - 1]);
            string typeName, field = null;
            if (parts.Length == 3 && parts[1] == "size") typeName = parts[0];
            else { var dot = parts[0].IndexOf('.'); typeName = parts[0].Substring(0, dot); field = parts[0].Substring(dot + 1); }

            var type = asm.GetType("Simitone.VR.OpenXR." + typeName);
            long actual;
            if (type == null) { Console.WriteLine($"MISSING TYPE {typeName}"); failures++; continue; }
            if (field == null) actual = Marshal.SizeOf(type);
            else
            {
                var fi = type.GetField(field, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (fi == null) { Console.WriteLine($"MISSING FIELD {typeName}.{field}"); failures++; continue; }
                actual = Marshal.OffsetOf(type, fi.Name).ToInt64();
            }
            checkedCount++;
            if (actual != expected) { Console.WriteLine($"MISMATCH {parts[0]}: C {expected}, C# {actual}"); failures++; }
        }
        Console.WriteLine($"{checkedCount} layout checks, {failures} failures");
        return failures == 0 ? 0 : 1;
    }
}
