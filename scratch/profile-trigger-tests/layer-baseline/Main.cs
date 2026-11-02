using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using WinPieGestures;

internal static class BaselineMain
{
    private static int Main()
    {
        string directory = typeof(BaselineMain).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(x => x.Key == "StarPieCandidateDir").Value!;
        string expected = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, "StarPie.dll"))));
        string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(WheelProfile).Assembly.Location)));
        if (expected != actual) throw new InvalidOperationException("Wrong candidate; no behavioral result accepted.");
        Console.WriteLine($"HOST SHA256={actual}");
        try { LayerBaselineScenario.Run(); Console.WriteLine("PASS layer JSON routing"); return 0; }
        catch (InvalidOperationException e) { Console.WriteLine($"BEHAVIOR FAIL: {e.Message}"); return 1; }
    }
}
