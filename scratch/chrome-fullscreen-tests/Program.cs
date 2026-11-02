using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using WinPieGestures;

internal static class Program
{
    private const int Caption = 0x00C00000;
    private const int Maximized = 0x01000000;
    private const int Borderless = unchecked((int)0x90000000);
    private static int _checks;

    private static int Main()
    {
        try
        {
            string candidate = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "StarPieCandidateDir").Value!;
            string expectedPath = Path.Combine(candidate, "StarPie.dll");
            string loadedPath = typeof(FullScreenHelper).Assembly.Location;
            Check(Path.GetFullPath(loadedPath).Equals(Path.Combine(AppContext.BaseDirectory, "StarPie.dll"), StringComparison.OrdinalIgnoreCase),
                "host loads from this fresh test output");
            string expectedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(expectedPath)));
            Check(expectedHash == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(loadedPath))), "candidate SHA-256 matches loaded host");
            Console.WriteLine($"CANDIDATE: {expectedPath}; SHA256={expectedHash}");

            var monitor = Bounds(0, 0, 1920, 1080);
            Check(!Classify(Bounds(-8, -8, 1928, 1088), monitor, Caption | Maximized),
                "bordered maximized window covering a monitor is not fullscreen");
            Check(!Classify(monitor, monitor, Caption | Maximized), "exact-size bordered maximized window is not fullscreen");
            Check(!Classify(Bounds(-8, -8, 1928, 1048), monitor, Caption | Maximized), "maximized window above a visible taskbar is not fullscreen");
            Check(Classify(monitor, monitor, Borderless), "borderless fullscreen remains suppressed");
            Check(Classify(monitor, monitor, Borderless | Maximized), "borderless maximized/F11-style fullscreen remains suppressed");
            Check(Classify(Bounds(-8, -8, 1928, 1088), monitor, Borderless), "oversized borderless fullscreen retains previous behavior");
            Check(Classify(monitor, monitor, Caption), "non-maximized monitor-covering window retains previous behavior");
            Check(!Classify(Bounds(100, 100, 1400, 900), monitor, Borderless), "ordinary window is not fullscreen");
            Check(!Classify(Bounds(0, 0, 1919, 1080), monitor, Borderless), "partial monitor coverage is not fullscreen");
            foreach (int exStyle in new[] { 0x00080000, 0x00000020, 0x00080020 })
            {
                Check(!FullScreenHelper.IsFullScreenSnapshot(monitor, monitor, Borderless, exStyle), "layered/transparent overlay is excluded");
            }

            foreach ((int left, int top, int width, int height) in new[]
            {
                (-1920, 0, 1920, 1080), (1920, -2160, 3840, 2160), (0, 0, 2560, 1440), (0, 0, 3840, 2160)
            })
            {
                var screen = Bounds(left, top, left + width, top + height);
                Check(Classify(screen, screen, Borderless | Maximized), "physical-pixel fullscreen works across monitor origins and sizes");
                Check(!Classify(Bounds(left - 12, top - 12, left + width + 12, top + height + 12), screen, Caption | Maximized),
                    "bordered maximized frame is excluded at different DPI/origins");
            }

            Console.WriteLine($"PASS: {_checks} fullscreen snapshot checks; no native window APIs, hooks or GUI were used.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL after {_checks} checks: {ex.Message}");
            return 1;
        }
    }

    private static FullScreenHelper.RECT Bounds(int left, int top, int right, int bottom) =>
        new() { Left = left, Top = top, Right = right, Bottom = bottom };

    private static bool Classify(FullScreenHelper.RECT window, FullScreenHelper.RECT monitor, int style) =>
        FullScreenHelper.IsFullScreenSnapshot(window, monitor, style, 0);

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        _checks++;
    }
}
