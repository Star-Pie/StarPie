using System.Diagnostics;
using System.Globalization;

namespace WinPieGestures;

/// <summary>Startup-only timings; no polling, process sampling, or hook-path work.</summary>
internal sealed class StartupDiagnostics
{
	private readonly long _started = Stopwatch.GetTimestamp();

	internal void Mark(string phase)
	{
		string elapsed = Stopwatch.GetElapsedTime(_started).TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture);
		AppLogger.LogInfo($"Startup phase={phase} elapsed_ms={elapsed}");
	}
}
