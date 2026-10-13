using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WinPieGestures;

/// <summary>
/// StarPie 内置自包含原生全盘极速搜索引擎
/// 100% 自包含零外部依赖，开箱即用；
/// 具备内存预加载常用应用层、桌面与工程极速扫描层，以及多盘并发广度优先穿透能力。
/// </summary>
public static class NativeSearchEngine
{
	private static readonly object _initLock = new object();
	private static bool _isInitialized;
	private static List<SearchResultItem> _cachedApps = new List<SearchResultItem>();
	private static DateTime _lastCacheTime = DateTime.MinValue;

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

	private static readonly HashSet<string> s_cadExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		".sldprt", ".sldasm", ".slddrw", ".step", ".stp", ".iges", ".igs",
		".dwg", ".dxf", ".prt", ".asm", ".catpart", ".x_t", ".x_b"
	};

	private static readonly HashSet<string> s_docExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		".md", ".txt", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
		".pdf", ".py", ".cs", ".cpp", ".c", ".h", ".json", ".xml", ".csv"
	};

	private static readonly HashSet<string> s_appExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		".exe", ".bat", ".cmd", ".ps1", ".lnk"
	};

	private static readonly HashSet<string> s_videoExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		".mp4", ".mkv", ".avi", ".mov", ".flv", ".wmv", ".rmvb", ".webm", ".ts", ".m4v", ".3gp", ".f4v"
	};

	/// <summary>
	/// 确保常用应用、全盘固定驱动器已安装软件与注册表索引初始化
	/// </summary>
	public static void EnsureCacheInitialized()
	{
		if (_isInitialized && (DateTime.Now - _lastCacheTime).TotalMinutes < 30)
		{
			return;
		}

		lock (_initLock)
		{
			if (_isInitialized && (DateTime.Now - _lastCacheTime).TotalMinutes < 30)
			{
				return;
			}

			var apps = new List<SearchResultItem>();
			var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			// 1. 系统核心控制预设
			apps.Add(new SearchResultItem
			{
				FullPath = @"C:\Windows\System32\Taskmgr.exe",
				FileName = "任务管理器 (Task Manager)",
				Extension = ".exe",
				Category = "System",
				CategoryDisplay = "系统工具",
				BadgeBg = "#1864748B",
				BadgeFg = "#64748B",
				IconEmoji = "📊"
			});
			apps.Add(new SearchResultItem
			{
				FullPath = @"control.exe",
				FileName = "控制面板 (Control Panel)",
				Extension = ".exe",
				Category = "System",
				CategoryDisplay = "系统设置",
				BadgeBg = "#1864748B",
				BadgeFg = "#64748B",
				IconEmoji = "⚙️"
			});
			apps.Add(new SearchResultItem
			{
				FullPath = @"calc.exe",
				FileName = "计算器 (Calculator)",
				Extension = ".exe",
				Category = "System",
				CategoryDisplay = "系统小工具",
				BadgeBg = "#183B82F6",
				BadgeFg = "#3B82F6",
				IconEmoji = "🔢"
			});
			apps.Add(new SearchResultItem
			{
				FullPath = @"explorer.exe",
				FileName = "文件资源管理器 (File Explorer)",
				Extension = ".exe",
				Category = "System",
				CategoryDisplay = "系统管理",
				BadgeBg = "#18EAB308",
				BadgeFg = "#EAB308",
				IconEmoji = "📁"
			});

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
								apps.Add(new SearchResultItem
								{
									FullPath = file.FullName,
									FileName = nameWithoutExt,
									Extension = ext,
									Size = file.Length,
									SizeFormatted = FormatFileSize(file.Length),
									DateModified = file.LastWriteTime,
									DateFormatted = file.LastWriteTime.ToString("yyyy-MM-dd"),
									IsFolder = false,
									Category = "App",
									CategoryDisplay = ext == ".lnk" ? "快捷方式" : "可执行程序",
									BadgeBg = "#183B82F6",
									BadgeFg = "#3B82F6",
									IconEmoji = "💻"
								});
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
										var fi = new FileInfo(cleanPath);
										apps.Add(new SearchResultItem
										{
											FullPath = cleanPath,
											FileName = name,
											Extension = ".exe",
											Size = fi.Length,
											SizeFormatted = FormatFileSize(fi.Length),
											DateModified = fi.LastWriteTime,
											DateFormatted = fi.LastWriteTime.ToString("yyyy-MM-dd"),
											IsFolder = false,
											Category = "App",
											CategoryDisplay = "已安装应用",
											BadgeBg = "#183B82F6",
											BadgeFg = "#3B82F6",
											IconEmoji = "💻"
										});
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
										var fi = new FileInfo(exe);
										apps.Add(new SearchResultItem
										{
											FullPath = exe,
											FileName = $"{dirName} ({exeName})",
											Extension = ".exe",
											Size = fi.Length,
											SizeFormatted = FormatFileSize(fi.Length),
											DateModified = fi.LastWriteTime,
											DateFormatted = fi.LastWriteTime.ToString("yyyy-MM-dd"),
											IsFolder = false,
											Category = "App",
											CategoryDisplay = "本地应用",
											BadgeBg = "#18F97316",
											BadgeFg = "#F97316",
											IconEmoji = "🚀"
										});
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
											var fi = new FileInfo(exe);
											apps.Add(new SearchResultItem
											{
												FullPath = exe,
												FileName = $"{exeName} ({dirName})",
												Extension = ".exe",
												Size = fi.Length,
												SizeFormatted = FormatFileSize(fi.Length),
												DateModified = fi.LastWriteTime,
												DateFormatted = fi.LastWriteTime.ToString("yyyy-MM-dd"),
												IsFolder = false,
												Category = "App",
												CategoryDisplay = "本地应用",
												BadgeBg = "#18F97316",
												BadgeFg = "#F97316",
												IconEmoji = "🚀"
											});
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

			Volatile.Write(ref _cachedApps, apps);
			_lastCacheTime = DateTime.Now;
			_isInitialized = true;
		}
	}

	/// <summary>
	/// 当搜索输入为空时，展示智能推荐与常用项目（告别冷冰冰的“未发现匹配文件”）
	/// </summary>
	public static List<SearchResultItem> GetInitialRecommendations(string category = "All")
	{
		EnsureCacheInitialized();

		var list = new List<SearchResultItem>();

		// 1. 优先放入高频应用程序与系统工具
		if (category == "All" || category == "App" || category == "System")
		{
			var cachedApps = Volatile.Read(ref _cachedApps);
			var apps = cachedApps.Where(a => category == "All" || a.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).Take(15);
			list.AddRange(apps);
		}

		// 2. 放入桌面上的近期活跃文件与文件夹
		try
		{
			string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
			if (Directory.Exists(desktop))
			{
				var desktopInfo = new DirectoryInfo(desktop);
				var recentEntries = desktopInfo.EnumerateFileSystemInfos()
					.Where(e => !e.Attributes.HasFlag(FileAttributes.Hidden) && !e.Name.StartsWith("."))
					.OrderByDescending(e => e.LastWriteTime)
					.Take(18);

				foreach (var entry in recentEntries)
				{
					bool isFolder = entry is DirectoryInfo;
					string ext = isFolder ? "" : entry.Extension.ToLowerInvariant();
					if (ext == ".ini" || ext == ".tmp") continue;

					var (cat, catDisplay, badgeBg, badgeFg, emoji) = ClassifyEntry(entry.FullName, isFolder, ext);
					if (category == "All" || cat.Equals(category, StringComparison.OrdinalIgnoreCase))
					{
						long size = isFolder ? 0 : ((FileInfo)entry).Length;
						list.Add(new SearchResultItem
						{
							FullPath = entry.FullName,
							FileName = isFolder ? entry.Name : Path.GetFileName(entry.FullName),
							Extension = ext,
							Size = size,
							SizeFormatted = isFolder ? "" : FormatFileSize(size),
							DateModified = entry.LastWriteTime,
							DateFormatted = entry.LastWriteTime.ToString("yyyy-MM-dd"),
							IsFolder = isFolder,
							Category = cat,
							CategoryDisplay = catDisplay,
							BadgeBg = badgeBg,
							BadgeFg = badgeFg,
							IconEmoji = emoji
						});
					}
				}
			}
		}
		catch { }

		// 3. 放入常用根目录与项目工程目录
		if (category == "All" || category == "Folder")
		{
			string[] commonDirs = new[]
			{
				Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
				Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
				@"G:\Users\2 Better\Desktop\design"
			};
			foreach (var d in commonDirs)
			{
				if (Directory.Exists(d) && !list.Any(x => x.FullPath.Equals(d, StringComparison.OrdinalIgnoreCase)))
				{
					list.Add(new SearchResultItem
					{
						FullPath = d,
						FileName = Path.GetFileName(d),
						Extension = "",
						IsFolder = true,
						Category = "Folder",
						CategoryDisplay = "常用目录",
						BadgeBg = "#18EAB308",
						BadgeFg = "#EAB308",
						IconEmoji = "📁"
					});
				}
			}
		}

		// 4. 若选定了视频分类且列表较空，尝试检索用户视频库与下载目录中的近期视频
		if (category == "Video")
		{
			try
			{
				var videoDirs = new[]
				{
					Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
					Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos"),
					Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
					Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
				};
				foreach (var vd in videoDirs)
				{
					if (!Directory.Exists(vd)) continue;
					var dirInfo = new DirectoryInfo(vd);
					foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
					{
						string ext = file.Extension.ToLowerInvariant();
						if (s_videoExts.Contains(ext) && !list.Any(x => x.FullPath.Equals(file.FullName, StringComparison.OrdinalIgnoreCase)))
						{
							list.Add(new SearchResultItem
							{
								FullPath = file.FullName,
								FileName = file.Name,
								Extension = ext,
								Size = file.Length,
								SizeFormatted = FormatFileSize(file.Length),
								DateModified = file.LastWriteTime,
								DateFormatted = file.LastWriteTime.ToString("yyyy-MM-dd"),
								IsFolder = false,
								Category = "Video",
								CategoryDisplay = "视频媒体",
								BadgeBg = "#18EC4899",
								BadgeFg = "#EC4899",
								IconEmoji = "🎬"
							});
							if (list.Count >= 20) break;
						}
					}
					if (list.Count >= 20) break;
				}
			}
			catch { }
		}

		return list;
	}

	/// <summary>
	/// 清空搜索引擎静态缓存（在搜索窗口关闭或长时间闲置后调用，归还内存）
	/// </summary>
	public static void ClearCaches()
	{
		lock (_initLock)
		{
			Volatile.Write(ref _cachedApps, new List<SearchResultItem>());
			_isInitialized = false;
			_lastCacheTime = DateTime.MinValue;
		}
	}

	/// <summary>
	/// 内置原生极速检索引擎：分层递进、置信度短路与时间预算（Time-Budget）保护
	/// </summary>
	public static Task<List<SearchResultItem>> SearchAsync(string query, string category = "All", int maxResults = 80, CancellationToken token = default)
	{
		return Task.Run(() =>
		{
			EnsureCacheInitialized();

			var results = new List<SearchResultItem>();
			var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			string q = query.Trim();
			if (string.IsNullOrEmpty(q))
			{
				return GetInitialRecommendations(category);
			}

			// 第一层：从已缓存的应用和系统级预设中毫秒级匹配 (0ms ~ 1ms)
			if (category == "All" || category == "App" || category == "System")
			{
				var cachedApps = Volatile.Read(ref _cachedApps);
				var matchedApps = cachedApps.Where(a =>
				{
					if (category != "All" && !a.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
					{
						return false;
					}
					return a.FileName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
					       a.FullPath.Contains(q, StringComparison.OrdinalIgnoreCase);
				}).Take(40);

				foreach (var app in matchedApps)
				{
					if (seenPaths.Add(app.FullPath))
					{
						results.Add(app);
					}
				}

				// 若分类为 App 或 System，已索引的应用与系统工具库已完整覆盖，直接短路返回，绝不产生无谓磁盘 I/O
				if (category == "App" || category == "System")
				{
					return results;
				}
			}

			if (token.IsCancellationRequested || results.Count >= maxResults)
			{
				return results;
			}

			// 第二层（高频热点极速扫描）：桌面、下载、文档与常用工程极速优先扫描 (通常仅需 3-10ms)
			var priorityDirs = new List<string>();
			AddDirIfValid(priorityDirs, Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
			AddDirIfValid(priorityDirs, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
			AddDirIfValid(priorityDirs, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
			AddDirIfValid(priorityDirs, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
			AddDirIfValid(priorityDirs, @"G:\Users\2 Better\Desktop\design");

			foreach (var pDir in priorityDirs)
			{
				if (token.IsCancellationRequested || results.Count >= maxResults) break;
				if (!Directory.Exists(pDir)) continue;

				try
				{
					foreach (var entry in Directory.EnumerateFileSystemEntries(pDir))
					{
						if (token.IsCancellationRequested || results.Count >= maxResults) break;
						string name = Path.GetFileName(entry);
						if (string.IsNullOrEmpty(name) || name.StartsWith(".")) continue;

						if (name.Contains(q, StringComparison.OrdinalIgnoreCase))
						{
							bool isDir = Directory.Exists(entry);
							string ext = isDir ? "" : Path.GetExtension(entry).ToLowerInvariant();
							var (cat, catDisplay, badgeBg, badgeFg, emoji) = ClassifyEntry(entry, isDir, ext);

							if (category == "All" || cat.Equals(category, StringComparison.OrdinalIgnoreCase))
							{
								if (seenPaths.Add(entry))
								{
									long size = 0;
									DateTime dateModified = DateTime.MinValue;
									try
									{
										if (!isDir)
										{
											var fi = new FileInfo(entry);
											size = fi.Length;
											dateModified = fi.LastWriteTime;
										}
										else
										{
											dateModified = Directory.GetLastWriteTime(entry);
										}
									}
									catch { }

									results.Add(new SearchResultItem
									{
										FullPath = entry,
										FileName = name,
										Extension = ext,
										Size = size,
										SizeFormatted = isDir ? "" : FormatFileSize(size),
										DateModified = dateModified,
										DateFormatted = dateModified != DateTime.MinValue ? dateModified.ToString("yyyy-MM-dd") : "",
										IsFolder = isDir,
										Category = cat,
										CategoryDisplay = catDisplay,
										BadgeBg = badgeBg,
										BadgeFg = badgeFg,
										IconEmoji = emoji,
										EngineSource = "Native"
									});
								}
							}
						}
					}
				}
				catch { }
			}

			// 若在高频目录与应用中已找到充分结果 (>= 20项)，即刻短路返回，保障极致盲操丝滑度
			if (token.IsCancellationRequested || results.Count >= 20 || results.Count >= maxResults)
			{
				return results;
			}

			// 第三层：带严格时间预算（Time-Budget）与收敛深度的并发磁盘穿透扫描
			// 单次交互搜索硬控在 80ms 时间预算内，深度收敛至 4 层，杜绝数万目录地毯式扫描导致的 5~9 秒失控卡顿
			var searchRoots = new List<string>();
			try
			{
				foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
				{
					searchRoots.Add(drive.RootDirectory.FullName);
				}
			}
			catch { }

			var syncLock = new object();
			var swBudget = Stopwatch.StartNew();
			const int maxTimeBudgetMs = 50;    // 50ms 时间预算硬控（保证 3 帧内极速出结果）
			const int maxDepth = 4;            // 实用深度 4 层（足够穿透绝大部分项目与多级文档）
			const int maxVisitedPerRoot = 200; // 每盘上限 200 目录

			Parallel.ForEach(searchRoots, new ParallelOptions
			{
				MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 4),
				CancellationToken = token
			}, (root, state) =>
			{
				if (token.IsCancellationRequested || swBudget.ElapsedMilliseconds > maxTimeBudgetMs)
				{
					state.Stop();
					return;
				}

				var queue = new Queue<(string path, int depth)>();
				queue.Enqueue((root, 0));
				int visited = 0;

				while (queue.Count > 0 && visited < maxVisitedPerRoot)
				{
					if (token.IsCancellationRequested || swBudget.ElapsedMilliseconds > maxTimeBudgetMs)
					{
						state.Stop();
						break;
					}

					lock (syncLock)
					{
						if (results.Count >= maxResults || results.Count >= 40)
						{
							state.Stop();
							break;
						}
					}

					var (curDir, depth) = queue.Dequeue();
					visited++;

					try
					{
						foreach (string fullPath in Directory.EnumerateFileSystemEntries(curDir))
						{
							if (token.IsCancellationRequested || swBudget.ElapsedMilliseconds > maxTimeBudgetMs)
							{
								state.Stop();
								break;
							}

							lock (syncLock)
							{
								if (results.Count >= maxResults || results.Count >= 40)
								{
									state.Stop();
									break;
								}
							}

							string name = Path.GetFileName(fullPath);
							if (string.IsNullOrEmpty(name) || name.StartsWith("."))
							{
								continue;
							}

							FileAttributes attr;
							try
							{
								attr = File.GetAttributes(fullPath);
							}
							catch
							{
								continue;
							}

							if (attr.HasFlag(FileAttributes.Hidden))
							{
								continue;
							}

							bool isFolder = attr.HasFlag(FileAttributes.Directory);
							string ext = isFolder ? "" : Path.GetExtension(name).ToLowerInvariant();

							if (name.Contains(q, StringComparison.OrdinalIgnoreCase))
							{
								var (cat, catDisplay, badgeBg, badgeFg, emoji) = ClassifyEntry(fullPath, isFolder, ext);

								if (category == "All" || cat.Equals(category, StringComparison.OrdinalIgnoreCase))
								{
									lock (syncLock)
									{
										if (seenPaths.Add(fullPath))
										{
											long size = 0;
											DateTime dateModified = DateTime.MinValue;
											try
											{
												if (!isFolder)
												{
													var fi = new FileInfo(fullPath);
													size = fi.Length;
													dateModified = fi.LastWriteTime;
												}
												else
												{
													dateModified = Directory.GetLastWriteTime(fullPath);
												}
											}
											catch { }

											results.Add(new SearchResultItem
											{
												FullPath = fullPath,
												FileName = name,
												Extension = ext,
												Size = size,
												SizeFormatted = isFolder ? "" : FormatFileSize(size),
												DateModified = dateModified,
												DateFormatted = dateModified != DateTime.MinValue ? dateModified.ToString("yyyy-MM-dd") : "",
												IsFolder = isFolder,
												Category = cat,
												CategoryDisplay = catDisplay,
												BadgeBg = badgeBg,
												BadgeFg = badgeFg,
												IconEmoji = emoji,
												EngineSource = "Native"
											});

											if (results.Count >= maxResults || results.Count >= 40)
											{
												state.Stop();
												break;
											}
										}
									}
								}
							}

							// 子目录入队：收敛至实用 4 层深度，过滤冗余垃圾目录
							if (isFolder && depth < maxDepth)
							{
								if (!s_ignoredDirs.Contains(name))
								{
									queue.Enqueue((fullPath, depth + 1));
								}
							}
						}
					}
					catch { }
				}
			});

			return results;
		}, token);
	}

	private static void AddDirIfValid(List<string> list, string dir)
	{
		if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir) && !list.Contains(dir, StringComparer.OrdinalIgnoreCase))
		{
			list.Add(dir);
		}
	}

	private static (string cat, string catDisplay, string badgeBg, string badgeFg, string emoji) ClassifyEntry(string fullPath, bool isFolder, string ext)
	{
		if (isFolder)
		{
			return ("Folder", "文件夹", "#18EAB308", "#EAB308", "📁");
		}

		if (s_cadExts.Contains(ext))
		{
			return ext switch
			{
				".sldprt" => ("CAD", "SolidWorks 零件", "#188B5CF6", "#8B5CF6", "🔩"),
				".sldasm" => ("CAD", "SolidWorks 装配体", "#188B5CF6", "#8B5CF6", "⚙️"),
				".slddrw" => ("CAD", "SolidWorks 工程图", "#188B5CF6", "#8B5CF6", "📐"),
				".dwg" or ".dxf" => ("CAD", "AutoCAD 图纸", "#1806B6D4", "#06B6D4", "📏"),
				".step" or ".stp" or ".iges" or ".igs" => ("CAD", "3D 通用模型", "#1806B6D4", "#06B6D4", "📐"),
				_ => ("CAD", "三维CAD工程", "#188B5CF6", "#8B5CF6", "📐")
			};
		}

		if (s_appExts.Contains(ext))
		{
			return ext switch
			{
				".lnk" => ("App", "快捷方式", "#183B82F6", "#3B82F6", "💻"),
				".bat" or ".cmd" or ".ps1" => ("App", "系统脚本", "#183B82F6", "#3B82F6", "⚡"),
				_ => ("App", "应用程序", "#183B82F6", "#3B82F6", "🚀")
			};
		}

		if (s_docExts.Contains(ext))
		{
			return ext switch
			{
				".md" => ("Doc", "Markdown", "#183B82F6", "#3B82F6", "📝"),
				".doc" or ".docx" => ("Doc", "Word 文档", "#182563EB", "#2563EB", "📄"),
				".xls" or ".xlsx" or ".csv" => ("Doc", "Excel 表格", "#1810B981", "#10B981", "📊"),
				".ppt" or ".pptx" => ("Doc", "PowerPoint", "#18F97316", "#F97316", "📑"),
				".pdf" => ("Doc", "PDF 电子书", "#18EF4444", "#EF4444", "📕"),
				".py" => ("Doc", "Python 源码", "#18F59E0B", "#F59E0B", "🐍"),
				".cs" => ("Doc", "C# 源码", "#188B5CF6", "#8B5CF6", "💻"),
				_ => ("Doc", "文本/代码", "#1864748B", "#64748B", "📋")
			};
		}

		if (s_videoExts.Contains(ext))
		{
			return ("Video", "视频媒体", "#18EC4899", "#EC4899", "🎬");
		}

		if (ext == ".cpl" || ext == ".msc")
		{
			return ("System", "系统管理", "#1864748B", "#64748B", "⚙️");
		}

		return ("Other", ext.TrimStart('.').ToUpperInvariant() + " 文件", "#1464748B", "#64748B", "📄");
	}

	private static string FormatFileSize(long bytes)
	{
		if (bytes < 1024) return $"{bytes} B";
		if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
		if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
		return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
	}
}
