using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WinPieGestures;

public static class AppLogger
{
	private static readonly string LogDirectory;
	private static readonly object FileLock = new object();
	private static readonly ConcurrentQueue<string> LogQueue = new ConcurrentQueue<string>();
	private static readonly AutoResetEvent LogSignal = new AutoResetEvent(false);
	private static readonly Thread WriterThread;
	private static volatile bool _isRunning = true;

	static AppLogger()
	{
		try
		{
			LogDirectory = Path.Combine(ApplicationDataPaths.Root, "logs");
			if (!Directory.Exists(LogDirectory))
			{
				Directory.CreateDirectory(LogDirectory);
			}
		}
		catch when (!ApplicationDataPaths.IsPortable)
		{
			LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
			try
			{
				if (!Directory.Exists(LogDirectory))
				{
					Directory.CreateDirectory(LogDirectory);
				}
			}
			catch { }
		}

		WriterThread = new Thread(ProcessLogQueue)
		{
			IsBackground = true,
			Name = "StarPie.Logger",
			Priority = ThreadPriority.Lowest
		};
		WriterThread.Start();

		CleanOldLogs(7);
	}

	public static string GetLogFolderPath() => LogDirectory;

	public static string GetTodayLogFilePath()
	{
		string today = DateTime.Now.ToString("yyyy-MM-dd");
		return Path.Combine(LogDirectory, $"starpie_{today}.log");
	}

	public static void LogInfo(string message) => Enqueue("INFO", message);
	public static void LogWarn(string message) => Enqueue("WARN", message);
	public static void LogDebug(string message) => Enqueue("DEBUG", message);

	public static void LogError(string message, Exception? ex = null)
	{
		StringBuilder sb = new StringBuilder(message);
		if (ex != null)
		{
			sb.AppendLine();
			sb.Append($"[Exception]: {ex.GetType().FullName}: {ex.Message}");
			sb.AppendLine();
			sb.Append($"[StackTrace]: {ex.StackTrace}");
		}
		Enqueue("ERROR", sb.ToString());
	}

	private static void Enqueue(string level, string message)
	{
		if (!_isRunning) return;
		string threadName = Thread.CurrentThread.Name ?? $"Thread-{Thread.CurrentThread.ManagedThreadId}";
		string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
		string entry = $"[{timestamp}] [{level,-5}] [{threadName}] {message}";
		LogQueue.Enqueue(entry);
		try
		{
			LogSignal.Set();
		}
		catch { }
	}

	private static void ProcessLogQueue()
	{
		while (_isRunning)
		{
			try
			{
				LogSignal.WaitOne(2000);
				FlushQueueToFile();
			}
			catch
			{
			}
		}
		FlushQueueToFile();
	}

	private static void FlushQueueToFile()
	{
		if (LogQueue.IsEmpty) return;

		string filePath = GetTodayLogFilePath();
		StringBuilder batch = new StringBuilder();

		while (LogQueue.TryDequeue(out string? entry))
		{
			if (!string.IsNullOrEmpty(entry))
			{
				batch.AppendLine(entry);
			}
		}

		if (batch.Length == 0) return;

		lock (FileLock)
		{
			try
			{
				File.AppendAllText(filePath, batch.ToString(), Encoding.UTF8);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[AppLogger Flush Error]: {ex.Message}");
			}
		}
	}

	public static void CleanOldLogs(int maxAgeDays = 7)
	{
		try
		{
			if (!Directory.Exists(LogDirectory)) return;
			DateTime cutoff = DateTime.Now.AddDays(-maxAgeDays);
			string[] files = Directory.GetFiles(LogDirectory, "starpie_*.log");
			foreach (string file in files)
			{
				FileInfo fi = new FileInfo(file);
				if (fi.LastWriteTime < cutoff)
				{
					try
					{
						fi.Delete();
					}
					catch { }
				}
			}
		}
		catch { }
	}

	public static void OpenLogFolder()
	{
		try
		{
			if (!Directory.Exists(LogDirectory))
			{
				Directory.CreateDirectory(LogDirectory);
			}
			Process.Start(new ProcessStartInfo
			{
				FileName = "explorer.exe",
				Arguments = $"\"{LogDirectory}\"",
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			LogError("Failed to open log folder in explorer", ex);
		}
	}

	public static void OpenTodayLogFile()
	{
		try
		{
			string filePath = GetTodayLogFilePath();
			if (!File.Exists(filePath))
			{
				lock (FileLock)
				{
					File.WriteAllText(filePath, $"=== StarPie Log Initialized at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\r\n", Encoding.UTF8);
				}
			}
			Process.Start(new ProcessStartInfo
			{
				FileName = filePath,
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			LogError("Failed to open today log file", ex);
		}
	}

	public static void Shutdown()
	{
		_isRunning = false;
		try
		{
			LogSignal.Set();
			WriterThread.Join(1000);
		}
		catch { }
		FlushQueueToFile();
	}
}
