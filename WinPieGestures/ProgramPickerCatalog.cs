using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WinPieGestures;

internal sealed record ProgramCandidate(string FullPath, string FileName);

// The program picker retains its existing sources for installed and portable applications.
internal static class ProgramPickerCatalog
{
	private static readonly object Gate = new();
	private static List<ProgramCandidate>? _apps;
	private static DateTime _expires;
	private static readonly HashSet<string> s_ignoredDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"$Recycle.Bin",
		"System Volume Information",
		"node_modules",
		".git",
		".vs",
		".idea",
		".gradle",
		".nuget",
		"WinSxS",
		"$WinREAgent",
		"DumpStack.log.tmp",
		"Recovery",
		"MSOCache"
	};

	public static Task<List<ProgramCandidate>> SearchAsync(string query, CancellationToken token = default) =>
		Task.Run(() =>
		{
			var apps = Snapshot();
			token.ThrowIfCancellationRequested();
			string text = query.Trim();
			return apps.Where(app => app.FileName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
				app.FullPath.Contains(text, StringComparison.OrdinalIgnoreCase)).Take(text.Length == 0 ? 15 : 40).ToList();
		}, token);

	public static void ClearCaches()
	{
		lock (Gate) _apps = null;
	}

	private static List<ProgramCandidate> Snapshot()
	{
		lock (Gate)
		{
			if (_apps != null && DateTime.UtcNow < _expires) return _apps;
			_apps = Discover();
			_expires = DateTime.UtcNow.AddMinutes(30);
			return _apps;
		}
	}

	private static List<ProgramCandidate> Discover()
	{
		var apps = new List<ProgramCandidate>();
		var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		// 2. 收集桌面与开始菜单快捷方式
		var searchDirs = new List<string>();
		AddDirIfValid(searchDirs, Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
		AddDirIfValid(searchDirs, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
		AddDirIfValid(searchDirs, Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
		AddDirIfValid(searchDirs, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));

		foreach (var dir in searchDirs)
		{
			try
			{
				var dirInfo = new DirectoryInfo(dir);
				foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
				{
					string ext = file.Extension.ToLowerInvariant();
					if (ext == ".lnk" || ext == ".exe")
					{
						string nameWithoutExt = Path.GetFileNameWithoutExtension(file.Name);
						if (nameWithoutExt.Contains("uninstall", StringComparison.OrdinalIgnoreCase) ||
						    nameWithoutExt.Contains("卸载", StringComparison.OrdinalIgnoreCase) ||
						    nameWithoutExt.Contains("update", StringComparison.OrdinalIgnoreCase))
						{
							continue;
						}

						if (seenPaths.Add(file.FullName))
						{
							apps.Add(new ProgramCandidate(file.FullName, nameWithoutExt));
						}
					}
				}
			}
			catch { }
		}

		// 3. 收集 64 位与 32 位注册表 App Paths
		(Microsoft.Win32.RegistryHive, Microsoft.Win32.RegistryView)[] hives = new[]
		{
			(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64),
			(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry32),
			(Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryView.Default)
		};
		foreach (var (hKey, view) in hives)
		{
			try
			{
				using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(hKey, view);
				using var appPathsKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
				if (appPathsKey != null)
				{
					foreach (var subKeyName in appPathsKey.GetSubKeyNames())
					{
						try
						{
							using var subKey = appPathsKey.OpenSubKey(subKeyName);
							string? pathVal = subKey?.GetValue("")?.ToString();
							if (!string.IsNullOrEmpty(pathVal))
							{
								string cleanPath = Environment.ExpandEnvironmentVariables(pathVal.Trim().Trim('"'));
								if (File.Exists(cleanPath) && seenPaths.Add(cleanPath))
								{
									string name = Path.GetFileNameWithoutExtension(subKeyName);
									apps.Add(new ProgramCandidate(cleanPath, name));
								}
							}
						}
						catch { }
					}
				}
			}
			catch { }
		}

		// 4. 深度扫描各固定驱动器根级与子级程序目录（如 H:\PS2024, K:\QQ, H:\bilibili 等便携或免安装程序）
		try
		{
			foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
			{
				try
				{
					foreach (var dir in Directory.GetDirectories(drive.RootDirectory.FullName))
					{
						string dirName = Path.GetFileName(dir);
						if (string.IsNullOrEmpty(dirName) || s_ignoredDirs.Contains(dirName) || dirName.StartsWith("$") || dirName.Equals("Windows", StringComparison.OrdinalIgnoreCase))
						{
							continue;
						}

						// 根目录下直接的 exe
						try
						{
							foreach (var exe in Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly))
							{
								string exeName = Path.GetFileNameWithoutExtension(exe);
								if (exeName.Contains("unins", StringComparison.OrdinalIgnoreCase) || exeName.Contains("setup", StringComparison.OrdinalIgnoreCase) || exeName.Contains("helper", StringComparison.OrdinalIgnoreCase))
								{
									continue;
								}

								if (seenPaths.Add(exe))
								{
									apps.Add(new ProgramCandidate(exe, $"{dirName} ({exeName})"));
								}
							}
						}
						catch { }

						// 1 级子目录下的 exe（如 H:\PS2024\Adobe Photoshop 2024\Photoshop.exe 或 K:\QQ\Bin\QQ.exe）
						try
						{
							foreach (var subDir in Directory.GetDirectories(dir))
							{
								string subName = Path.GetFileName(subDir);
								if (subName.StartsWith(".") || s_ignoredDirs.Contains(subName)) continue;

								foreach (var exe in Directory.GetFiles(subDir, "*.exe", SearchOption.TopDirectoryOnly))
								{
									string exeName = Path.GetFileNameWithoutExtension(exe);
									if (exeName.Contains("unins", StringComparison.OrdinalIgnoreCase) || exeName.Contains("setup", StringComparison.OrdinalIgnoreCase) || exeName.Contains("crash", StringComparison.OrdinalIgnoreCase))
									{
										continue;
									}

									if (seenPaths.Add(exe))
									{
										apps.Add(new ProgramCandidate(exe, $"{exeName} ({dirName})"));
									}
								}
							}
						}
						catch { }
					}
				}
				catch { }
			}
		}
		catch { }
		return apps;
	}

	private static void AddDirIfValid(List<string> list, string dir)
	{
		if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir) && !list.Contains(dir, StringComparer.OrdinalIgnoreCase))
		{
			list.Add(dir);
		}
	}
}
