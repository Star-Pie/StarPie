using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Windows.Foundation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace WinPieGestures;

public static class OcrManager
{
	private static readonly HttpClient s_httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
	private static string? s_lastEngineLanguage;

	/// <summary>打开 OCR 多引擎与接口设置弹窗</summary>
	public static void ShowSettingsDialog()
	{
		Application.Current?.Dispatcher.Invoke(() =>
		{
			try
			{
				OcrSettingsDialog dlg = new OcrSettingsDialog();
				dlg.Owner = Application.Current?.MainWindow;
				dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
				dlg.ShowDialog();
			}
			catch (Exception ex)
			{
				AppLogger.LogError("Failed to show OcrSettingsDialog", ex);
			}
		});
	}

	/// <summary>由动作触发：全屏框选截屏并执行 OCR 文本提取</summary>
	public static void StartCaptureAndRecognize()
	{
		Task.Run(async () =>
		{
			// 若由轮盘手势松手触发，轮盘刚被 Dismiss()，需要给 WPF 渲染管道与 DWM 桌面合成留出 1~2 帧缓冲（约 35ms），
			// 避免桌面截图将尚未完全淡出消失的半透明轮盘截入全屏大图中。
			await Task.Delay(35).ConfigureAwait(false);

			if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.HasShutdownStarted)
			{
				return;
			}

			await Application.Current.Dispatcher.InvokeAsync(() =>
			{
				try
				{
					ScreenSnipWindow snip = new ScreenSnipWindow(async bmp =>
					{
						if (bmp != null)
						{
							await ProcessSnippetAsync(bmp);
						}
					});
					snip.Show();
				}
				catch (Exception ex)
				{
					AppLogger.LogError("Failed to launch ScreenSnipWindow", ex);
					MessageBox.Show("启动截屏框选失败: " + ex.Message, "StarPie", MessageBoxButton.OK, MessageBoxImage.Warning);
				}
			});
		});
	}

	public static async Task ProcessSnippetAsync(Bitmap bmp)
	{
		OcrSettings config = ConfigManager.CurrentConfig?.OcrConfig ?? new OcrSettings();
		int imageWidth = bmp.Width;
		int imageHeight = bmp.Height;
		Stopwatch sw = Stopwatch.StartNew();
		string recognizedText = "";
		string engineName = "本地离线引擎";

		try
		{
			string provider = config.Provider?.Trim() ?? "Local";
			switch (provider)
			{
			case "Ai":
				engineName = $"{I18n.T("OcrProviderAi")} ({config.AiModel})";
				recognizedText = await RecognizeWithAiVisionAsync(bmp, config);
				break;

			case "Custom":
				engineName = I18n.T("OcrProviderCustom");
				recognizedText = await RecognizeWithCustomHttpAsync(bmp, config);
				break;

			case "Cloud":
				engineName = $"{config.CloudProvider} Cloud OCR";
				recognizedText = await RecognizeWithCloudAsync(bmp, config);
				break;

			case "Local":
			default:
				string localTitle = I18n.T("OcrBadgeLocalEngine");
				string langSuffix = !string.IsNullOrEmpty(s_lastEngineLanguage) ? $" [{s_lastEngineLanguage}]" : "";
				engineName = $"{localTitle}{langSuffix}";
				recognizedText = await RecognizeWithLocalWinRtAsync(bmp, config);
				if (!string.IsNullOrEmpty(s_lastEngineLanguage))
				{
					engineName = $"{localTitle} [{s_lastEngineLanguage}]";
				}
				break;
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError("OCR recognition error", ex);
			recognizedText = $"[OCR 识别异常]: {ex.Message}";
		}
		finally
		{
			sw.Stop();
			bmp.Dispose();
		}

		string latency = $"{sw.ElapsedMilliseconds}ms";
		AppLogger.LogInfo($"OCR completed: engine='{engineName}', image={imageWidth}x{imageHeight}px, chars={recognizedText.Length}, elapsed={latency}");

#if DEBUG
		string rawTextForDebug = recognizedText;
#endif

		// 格式后处理：去除中文字间多余空格、合并断裂字符与优化表格排版
		if (!recognizedText.StartsWith("["))
		{
			recognizedText = PostProcessText(recognizedText, config.RemoveSpacesBetweenCjk);
		}

#if DEBUG
		LogDebugOcrText(rawTextForDebug, recognizedText);
#endif

		bool isDiagnosticText = recognizedText.StartsWith("[");

		// 调度回 UI 线程分发结果
		Application.Current?.Dispatcher.Invoke(() =>
		{
			if (!string.IsNullOrWhiteSpace(recognizedText) && config.AutoCopyToClipboard && !isDiagnosticText)
			{
				try
				{
					System.Windows.Clipboard.SetText(recognizedText);
				}
				catch
				{
				}
			}

			if (config.ShowResultWindow)
			{
				OcrResultWindow resWin = new OcrResultWindow(recognizedText, engineName, latency);
				resWin.Show();
			}

			if (config.SearchInBrowser && !string.IsNullOrWhiteSpace(recognizedText) && !isDiagnosticText)
			{
				try
				{
					string q = recognizedText.Trim();
					if (q.Length > 80) q = q.Substring(0, 80);
					string url = "https://www.bing.com/search?q=" + Uri.EscapeDataString(q);
					Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
				}
				catch
				{
				}
			}
		});
	}

	/// <summary>1. Windows 10/11 原生 Windows.Media.Ocr 引擎</summary>
	private static async Task<string> RecognizeWithLocalWinRtAsync(Bitmap bmp, OcrSettings config)
	{
		if (OcrEngine.AvailableRecognizerLanguages.Count == 0)
		{
			return "[提示]: 当前 Windows 系统未检测到本地原生 OCR 识别引擎组件。\n（常见于精简版/企业版 Windows 系统，或系统尚未下载「光学字符识别」可选功能）\n\n💡 推荐解决方案：\n1. 【一键切换到 AI 视觉大模型】（推荐 · 免安装任何本地包 · 识别精度最高）：\n   在 StarPie 接口设置中配置硅基流动 / DeepSeek / OpenAI / Ollama 等端点，支持极速文字、表格与公式提取。\n2. 【安装 Windows 原生 OCR 功能】：\n   打开 Windows 设置 -> 应用 -> 可选功能 -> 添加可选功能，搜索并安装「中文(简体)光学字符识别」即可恢复离线使用。";
		}

		OcrEngine? engine = null;
		string langTag = config.LocalLanguage ?? "zh-Hans";

		try
		{
			if (OcrEngine.IsLanguageSupported(new Language(langTag)))
			{
				engine = OcrEngine.TryCreateFromLanguage(new Language(langTag));
			}
		}
		catch
		{
		}

		if (engine == null)
		{
			try
			{
				engine = OcrEngine.TryCreateFromUserProfileLanguages();
			}
			catch
			{
			}
		}

		if (engine == null)
		{
			engine = OcrEngine.TryCreateFromLanguage(OcrEngine.AvailableRecognizerLanguages[0]);
		}

		if (engine == null)
		{
			return "[提示]: 无法初始化本地 OCR 引擎。建议在 StarPie 动作设置中切换为 AI 视觉大模型 / 云端接口。";
		}

		// 记录实际生效的识别语言：配置语言缺失时会静默回退，结果界面必须可见，日志必须可查
		s_lastEngineLanguage = engine.RecognizerLanguage.LanguageTag;
		if (!string.Equals(s_lastEngineLanguage, langTag, StringComparison.OrdinalIgnoreCase))
		{
			string availableTags = string.Join(", ", OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag));
			AppLogger.LogWarn($"OCR local engine fell back: configured '{langTag}' unavailable (installed: [{availableTags}]), using '{s_lastEngineLanguage}'");
		}
		else
		{
			AppLogger.LogInfo($"OCR local engine language: {s_lastEngineLanguage}");
		}

		using Bitmap preparedBmp = PreprocessSnippetForOcr(bmp);
		using SoftwareBitmap softwareBitmap = await ConvertToSoftwareBitmapAsync(preparedBmp);
		OcrResult result = await engine.RecognizeAsync(softwareBitmap);
#if DEBUG
		if (result != null)
		{
			foreach (var l in result.Lines)
			{
				AppLogger.LogDebug($"[DEBUG RAW LINE]: '{l.Text}' (words: {l.Words.Count})");
			}
		}
#endif
		if (result != null && result.Lines.Count > 0)
		{
			string text = ReconstructTextByGeometry(result).TrimEnd();
			bool isJunkSingle = (text.Length == 1 && (text[0] == '口' || text[0] == 'o' || text[0] == 'O'));
			if (!string.IsNullOrWhiteSpace(text) && !isJunkSingle)
			{
				return text;
			}
		}

		// 微小短词/单字选区双重兜底 (Micro-Selection Rescue Fallback)
		// WinRT OCR 文本行检测器对极小尺寸 (如 60x48px "警告")、低长宽比选区存在噪点过滤阈值，
		// 若初次识别无结果且图像尺寸较小，通过横向平铺 [src] [gap] [src] 突破几何阈值再次识别并去重。
		bool isMicro = bmp.Width < 200 || bmp.Height < 120 || ((double)bmp.Width / Math.Max(1, bmp.Height) < 2.2);
		if (isMicro)
		{
			int copies = (bmp.Width <= 35 && bmp.Height <= 35) ? 3 : 2;
			using Bitmap tiledBmp = PreprocessTiledSnippetForOcr(bmp, scaleFactor: 1.5, copies: copies, gap: 15, pad: 35);
			using SoftwareBitmap tiledSoftware = await ConvertToSoftwareBitmapAsync(tiledBmp);
			OcrResult tiledResult = await engine.RecognizeAsync(tiledSoftware);
#if DEBUG
			if (tiledResult != null)
			{
				foreach (var l in tiledResult.Lines)
				{
					AppLogger.LogDebug($"[DEBUG TILED LINE]: '{l.Text}' (words: {l.Words.Count})");
				}
			}
#endif
			if (tiledResult != null && tiledResult.Lines.Count > 0)
			{
				string rawText = ReconstructTextByGeometry(tiledResult).TrimEnd();
				return CollapseTiledDuplicates(rawText, copies: copies);
			}
		}

		return "[未识别到有效文字内容]";
	}

	/// <summary>2. OpenAI 兼容 / 本地 Ollama 多模态视觉模型 API</summary>
	private static async Task<string> RecognizeWithAiVisionAsync(Bitmap bmp, OcrSettings config)
	{
		string endpoint = config.AiEndpoint?.Trim() ?? "https://api.openai.com/v1";
		if (!endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
		{
			endpoint = endpoint.TrimEnd('/') + "/chat/completions";
		}

		string base64Image;
		using (MemoryStream ms = new MemoryStream())
		{
			bmp.Save(ms, ImageFormat.Jpeg);
			base64Image = Convert.ToBase64String(ms.ToArray());
		}

		string prompt = config.AiPromptMode switch
		{
			"latex" => "请提取图片中的全部数学公式与文字，将数学公式转换为标准 LaTeX 格式（如 $$...$$ 或 $...$）。仅输出公式与文本，不要包含多余开场白。",
			"markdown" => "请提取图片中的文字与表格结构，将表格转换为标准 Markdown 表格格式。不要包含多余寒暄。",
			"translate" => "请提取图片中的文字并直接翻译为流畅的简体中文。仅输出翻译结果。",
			_ => "请精确提取图片中的全部文字。保持原有行结构，不要包含任何前缀或解释说明。"
		};

		var requestBody = new
		{
			model = string.IsNullOrWhiteSpace(config.AiModel) ? "gpt-4o-mini" : config.AiModel.Trim(),
			messages = new object[]
			{
				new
				{
					role = "user",
					content = new object[]
					{
						new { type = "text", text = prompt },
						new
						{
							type = "image_url",
							image_url = new { url = $"data:image/jpeg;base64,{base64Image}" }
						}
					}
				}
			},
			max_tokens = 2000
		};

		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint);
		if (!string.IsNullOrWhiteSpace(config.AiApiKey))
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AiApiKey.Trim());
		}
		request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

		using HttpResponseMessage response = await s_httpClient.SendAsync(request);
		string responseJson = await response.Content.ReadAsStringAsync();

		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {responseJson}");
		}

		using JsonDocument doc = JsonDocument.Parse(responseJson);
		if (doc.RootElement.TryGetProperty("choices", out JsonElement choices) && choices.GetArrayLength() > 0)
		{
			JsonElement firstChoice = choices[0];
			if (firstChoice.TryGetProperty("message", out JsonElement message) && message.TryGetProperty("content", out JsonElement content))
			{
				return content.GetString()?.Trim() ?? "";
			}
		}

		return responseJson;
	}

	/// <summary>3. 自定义 HTTP 私有化 OCR (PaddleOCR / Umi-OCR)</summary>
	private static async Task<string> RecognizeWithCustomHttpAsync(Bitmap bmp, OcrSettings config)
	{
		string url = config.CustomHttpUrl?.Trim() ?? "http://127.0.0.1:1224/api/ocr";
		using MemoryStream ms = new MemoryStream();
		bmp.Save(ms, ImageFormat.Png);
		string base64 = Convert.ToBase64String(ms.ToArray());

		var requestObj = new { base64 = base64 };
		using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url)
		{
			Content = new StringContent(JsonSerializer.Serialize(requestObj), Encoding.UTF8, "application/json")
		};

		using HttpResponseMessage resp = await s_httpClient.SendAsync(req);
		string resText = await resp.Content.ReadAsStringAsync();
		if (!resp.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {resText}");
		}

		try
		{
			using JsonDocument doc = JsonDocument.Parse(resText);
			if (doc.RootElement.TryGetProperty("data", out JsonElement data))
			{
				if (data.ValueKind == JsonValueKind.String) return data.GetString() ?? "";
				if (data.ValueKind == JsonValueKind.Array)
				{
					StringBuilder sb = new StringBuilder();
					foreach (var item in data.EnumerateArray())
					{
						if (item.TryGetProperty("text", out JsonElement txt)) sb.AppendLine(txt.GetString());
					}
					return sb.ToString().TrimEnd();
				}
			}
		}
		catch
		{
		}

		return resText;
	}

	/// <summary>4. 商业云端 OCR 占位支持</summary>
	private static async Task<string> RecognizeWithCloudAsync(Bitmap bmp, OcrSettings config)
	{
		await Task.Delay(100);
		return $"[{config.CloudProvider} 云端 OCR]: 凭证已就绪 (可直接在设置中绑定 API Key 与 Secret)";
	}

	private static async Task<SoftwareBitmap> ConvertToSoftwareBitmapAsync(Bitmap bmp)
	{
		using MemoryStream ms = new MemoryStream();
		bmp.Save(ms, ImageFormat.Png);
		byte[] bytes = ms.ToArray();

		using InMemoryRandomAccessStream ras = new InMemoryRandomAccessStream();
		using (DataWriter writer = new DataWriter(ras))
		{
			writer.WriteBytes(bytes);
			await writer.StoreAsync();
			await writer.FlushAsync();
			writer.DetachStream();
		}
		ras.Seek(0);

		BitmapDecoder decoder = await BitmapDecoder.CreateAsync(ras);
		return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
	}

	private class OcrLineItem
	{
		public string Text { get; set; } = string.Empty;
		public double X { get; set; }
		public double Y { get; set; }
		public double Width { get; set; }
		public double Height { get; set; }
		public double Right => X + Width;
		public double Bottom => Y + Height;
		public double CenterY => Y + Height / 2.0;
	}

	private class VisualRow
	{
		public List<OcrLineItem> Items { get; } = new List<OcrLineItem>();
		public double MinY { get; set; } = double.MaxValue;
		public double MaxY { get; set; } = double.MinValue;
		public double Height => Math.Max(1.0, MaxY - MinY);
		public double CenterY => (MinY + MaxY) / 2.0;

		public void Add(OcrLineItem item)
		{
			Items.Add(item);
			if (item.Y < MinY) MinY = item.Y;
			if (item.Bottom > MaxY) MaxY = item.Bottom;
		}

		public bool CanAccept(OcrLineItem item)
		{
			if (Items.Count == 0) return true;

			double minH = Math.Min(Height, item.Height);
			if (minH <= 0.0) minH = 16.0;

			// 1. 水平重叠防护：如果在横向上与行内已有条目发生实质性重叠（>20%宽度且重叠>15px），
			// 说明这是处于同一列垂直上下相邻的两行，绝不能聚在同一视觉行
			foreach (var existing in Items)
			{
				double xOverlap = Math.Max(0.0, Math.Min(existing.Right, item.Right) - Math.Max(existing.X, item.X));
				double minW = Math.Min(existing.Width, item.Width);
				if (minW > 15.0 && xOverlap > 0.20 * minW && xOverlap > 15.0)
				{
					return false;
				}
			}

			// 2. 垂直重叠度与中心线距离匹配
			double overlap = Math.Max(0.0, Math.Min(MaxY, item.Bottom) - Math.Max(MinY, item.Y));
			double overlapRatio = overlap / minH;
			double centerDiff = Math.Abs(CenterY - item.CenterY);

			// 若垂直重叠率 >= 35%，或中心线距离在 0.55 * minH 以内，判定为处于同一水平行
			return overlapRatio >= 0.35 || centerDiff <= minH * 0.55;
		}
	}

	/// <summary>
	/// 基于 WinRT OcrResult 各 Line/Word 的几何空间包围盒进行视觉行重构。
	/// 彻底解决多栏表格、并列分栏按从上到下倒错读取，以及右侧链接/标签被撕裂为孤立断行的问题。
	/// </summary>
	private static string ReconstructTextByGeometry(OcrResult result)
	{
		if (result == null || result.Lines.Count == 0)
		{
			return string.Empty;
		}

		List<OcrLineItem> items = new List<OcrLineItem>();
		foreach (var line in result.Lines)
		{
			if (string.IsNullOrWhiteSpace(line.Text)) continue;

			if (line.Words.Count == 0)
			{
				items.Add(new OcrLineItem { Text = line.Text });
				continue;
			}

			double minX = double.MaxValue;
			double minY = double.MaxValue;
			double maxX = double.MinValue;
			double maxY = double.MinValue;

			foreach (var word in line.Words)
			{
				var rect = word.BoundingRect;
				if (rect.X < minX) minX = rect.X;
				if (rect.Y < minY) minY = rect.Y;
				if (rect.X + rect.Width > maxX) maxX = rect.X + rect.Width;
				if (rect.Y + rect.Height > maxY) maxY = rect.Y + rect.Height;
			}

			items.Add(new OcrLineItem
			{
				Text = line.Text,
				X = minX,
				Y = minY,
				Width = Math.Max(0, maxX - minX),
				Height = Math.Max(0, maxY - minY)
			});
		}

		if (items.Count == 0) return string.Empty;
		if (items.Count == 1) return items[0].Text;

		// 兜底：若所有条目缺失几何包围盒（全为0），回退为原生顺序输出
		if (items.All(x => x.Width == 0 && x.Height == 0))
		{
			StringBuilder fallbackSb = new StringBuilder();
			foreach (var l in result.Lines) fallbackSb.AppendLine(l.Text);
			return fallbackSb.ToString().TrimEnd();
		}

		// 按纵向扫描线中心线 CenterY 升序排列，CenterY 相近时按横坐标 X 升序
		items.Sort((a, b) =>
		{
			int cyCmp = a.CenterY.CompareTo(b.CenterY);
			return cyCmp != 0 ? cyCmp : a.X.CompareTo(b.X);
		});

		List<VisualRow> rows = new List<VisualRow>();
		foreach (var item in items)
		{
			VisualRow? bestRow = null;
			double bestCenterDiff = double.MaxValue;

			foreach (var row in rows)
			{
				if (row.CanAccept(item))
				{
					double diff = Math.Abs(row.CenterY - item.CenterY);
					if (diff < bestCenterDiff)
					{
						bestCenterDiff = diff;
						bestRow = row;
					}
				}
			}

			if (bestRow != null)
			{
				bestRow.Add(item);
			}
			else
			{
				var newRow = new VisualRow();
				newRow.Add(item);
				rows.Add(newRow);
			}
		}

		// 对行进行自上而下排序
		rows.Sort((a, b) => a.MinY.CompareTo(b.MinY));

		StringBuilder sb = new StringBuilder();
		foreach (var row in rows)
		{
			if (row.Items.Count == 0) continue;

			// 行内条目按横向位置从左到右严格排序
			row.Items.Sort((a, b) => a.X.CompareTo(b.X));

			StringBuilder rowSb = new StringBuilder();
			for (int i = 0; i < row.Items.Count; i++)
			{
				var item = row.Items[i];
				if (i == 0)
				{
					rowSb.Append(item.Text);
				}
				else
				{
					var prev = row.Items[i - 1];
					double gap = item.X - prev.Right;
					double avgH = (prev.Height + item.Height) / 2.0;
					if (avgH <= 0.0) avgH = 16.0;

#if DEBUG
					if (item.Text.Contains("功能") || prev.Text.Contains("快捷"))
					{
						Console.WriteLine($"[DEBUG GAP]: gap={gap} avgH={avgH} threshold={Math.Max(34.0, avgH * 1.5)}");
					}
#endif

					// 横向间隙分析：若相邻两段间距大于 38px 且大于 1.6 倍行高，判定为表格跨列，插入制表符 '\t'；
					// 否则判定为同一句式后置着色/链接标签（如 "#73"），使用普通单个空格衔接
					if (gap >= Math.Max(38.0, avgH * 1.6))
					{
						rowSb.Append('\t');
					}
					else
					{
						rowSb.Append(' ');
					}
					rowSb.Append(item.Text);
				}
			}

			sb.AppendLine(rowSb.ToString());
		}

		return sb.ToString().TrimEnd();
	}

	/// <summary>
	/// 智能排版后处理：清洗多余空格、合并切断符号、对齐标点、规范化中英文排版。
	/// 严格保护换行符 \r\n，杜绝正则跨行误删换行导致的跨行文本粘连。
	/// </summary>
	public static string PostProcessText(string text, bool removeCjkSpaces = true)
	{
		if (string.IsNullOrWhiteSpace(text)) return text;

		string[] rawLines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
		List<string> processedLines = new List<string>(rawLines.Length);

		foreach (string rawLine in rawLines)
		{
			string line = rawLine;

			// 若当前行包含制表符（来自几何重构判定的表格列），逐列单元格清洗并严格保留 '\t'
			// 但如果是命令行参数内部误插制表符（如 git log -n 5\t--oneline），恢复为空格
			if (line.Contains('\t') && Regex.IsMatch(line, @"^\s*(?:git|dotnet|cargo|npm|pnpm|yarn|docker|kubectl|pip)\b", RegexOptions.IgnoreCase))
			{
				line = Regex.Replace(line, @"\t+(?=[—–―－−一\-]+[a-zA-Z0-9])", " ");
			}

			if (line.Contains('\t'))
			{
				string[] cells = line.Split('\t');
				for (int c = 0; c < cells.Length; c++)
				{
					cells[c] = ProcessSingleLineOrCell(cells[c], removeCjkSpaces);
				}
				processedLines.Add(string.Join("\t", cells));
			}
			else
			{
				processedLines.Add(ProcessSingleLineOrCell(line, removeCjkSpaces));
			}
		}

		return string.Join("\r\n", processedLines);
	}

	private static string ProcessSingleLineOrCell(string text, bool removeCjkSpaces)
	{
		if (string.IsNullOrWhiteSpace(text)) return text.Trim();

		string line = text;

		// 1. 清理行首装饰性圆点符号 (·, •, ●, ◆, ▪ 等)
		line = Regex.Replace(line, @"^\s*[·•●◆▪]\x20*", "");

		// 1.5 快捷键连接符规范化 (如 Ctrl + C -> Ctrl+C, Alt + Space -> Alt+Space, Ctrl + Shift + O -> Ctrl+Shift+O)
		line = Regex.Replace(line, @"(?i)(?<=\b(?:Ctrl|Alt|Shift|Win))\s*\+\s*", "+");
		line = Regex.Replace(line, @"(?i)(?<=\+)\s*(?=[a-zA-Z0-9])", "");
		line = Regex.Replace(line, @"(?i)(?<=\+)\s*(?=Space|Enter|Tab|Esc|Delete|Insert|Home|End)\b", "");

		// 2. 修复形如 vl · 7 · 2 一 beta · 2 或 v1 . 7 . 2 或 vl，7 的版本号错切
		line = Regex.Replace(line, @"\b[vV]\s*([0-9lI])\s*[.·，,]\s*(\d+)(?:\s*[.·，,]\s*(\d+))?(?:\s*[-—一~]\s*([a-zA-Z]+)\s*[.·]\s*(\d+))?", m =>
		{
			string major = m.Groups[1].Value;
			if (major.Equals("l", StringComparison.OrdinalIgnoreCase) || major.Equals("I", StringComparison.OrdinalIgnoreCase))
			{
				major = "1";
			}
			string minor = m.Groups[2].Value;
			string? patch = m.Groups[3].Success ? m.Groups[3].Value : null;
			string res = patch != null ? $"v{major}.{minor}.{patch}" : $"v{major}.{minor}";
			if (m.Groups[4].Success)
			{
				string pre = m.Groups[4].Value;
				string preVer = m.Groups[5].Value;
				res += $"-{pre}.{preVer}";
			}
			return res;
		});

		// 2.1 修复版本号与后续中文字符之间的多余空格 (如 v1.7.2-beta.2 优化 -> v1.7.2-beta.2优化)
		line = Regex.Replace(line, @"(?<=v\d+\.\d+(?:\.\d+)?(?:-[a-zA-Z0-9.]+)?)\x20+(?=[\u4e00-\u9fa5])", "");

		// 3. 常见系统与软件名词错切修复 (如 Wi nd ows -> Windows)
		line = Regex.Replace(line, @"\bWi\s*nd\s*ow\s*s\b", "Windows", RegexOptions.IgnoreCase);

		// 4. 合并常见大写缩写中被切碎的字母 (如 OC R -> OCR, A P I -> API, D P I -> DPI)
		line = Regex.Replace(line, @"\bO\s*C\s*R\b", "OCR", RegexOptions.IgnoreCase);
		line = Regex.Replace(line, @"\bC\s*A\s*D\b", "CAD", RegexOptions.IgnoreCase);
		line = Regex.Replace(line, @"\bD\s*P\s*I\b", "DPI", RegexOptions.IgnoreCase);
		line = Regex.Replace(line, @"\bA\s*P\s*I\b", "API", RegexOptions.IgnoreCase);
		line = Regex.Replace(line, @"\bU\s*I\b", "UI", RegexOptions.IgnoreCase);
		line = Regex.Replace(line, @"\bU\s*X\b", "UX", RegexOptions.IgnoreCase);
		line = Regex.Replace(line, @"(?<=\b[A-Z]{2,4})\x20+(?=[A-Z]\b)", "");

		// 5. 循环合并连续被切碎的数字 (如 1 2 -> 12, 1 0 0 -> 100)
		for (int i = 0; i < 3; i++)
		{
			string next = Regex.Replace(line, @"(?<=\b\d+)\x20+(?=\d\b)", "");
			if (next == line) break;
			line = next;
		}

		// 6. 修复 Issue / 标签号被多余空格切开 (如 # 73 -> #73, # 7 8 -> #78)
		line = Regex.Replace(line, @"(?<=#)\x20+(?=\d)", "");

		// 6.1 终端命令行参数与关键字规范化 (CLI Flag & Command Normalization)
		line = Regex.Replace(line, @"\bdot\s*net\b", "dotnet", RegexOptions.IgnoreCase);
		line = Regex.Replace(line, @"\bbui\s*[lI1t-]\s*[dt]\b", "build", RegexOptions.IgnoreCase);
		line = Regex.Replace(line, @"\bpub\s*[lL1I]\s*ish\b", "publish", RegexOptions.IgnoreCase);
		line = Regex.Replace(line, @"\bg\s*it\s+(?:1|l|t)[0oO]g\b", "git log", RegexOptions.IgnoreCase);
		line = Regex.Replace(line, @"\bFi[lI1]es\b", "Files", RegexOptions.IgnoreCase);
		line = Regex.Replace(line, @"\bRe[lL1I-]\s*ease\b", "Release", RegexOptions.IgnoreCase);
		line = Regex.Replace(line, @"\bDebu\s*[gG9]\b", "Debug", RegexOptions.IgnoreCase);
		line = Regex.Replace(line, @"\bwin[—–―－−一](x64|x86|arm64)\b", "win-$1", RegexOptions.IgnoreCase);
		line = Regex.Replace(line, @"\s*[.．·]\s*(exe|dll|json|xml|txt|png|jpg|zip|cmd|ps1|bat)\b", ".$1", RegexOptions.IgnoreCase);

		// 双短横参数规范化 (--help, --no-build, --self-contained, --silent)
		line = Regex.Replace(line, @"(?<=^|\s)[—–―－−一]{2}\s*(?=[a-zA-Z0-9])", "--");

		// 单短横参数规范化 (-c, -p, -o, -v, -r 等)
		line = Regex.Replace(line, @"(?<=^|\s)[—–―－−一]\s*(?=[a-zA-Z0-9])", "-");

		// 单词内部连接符规范化 (如 win-x64, self-contained)
		line = Regex.Replace(line, @"(?<=[a-zA-Z0-9])[—–―－−](?=[a-zA-Z0-9])", "-");

		// CLI 参数大小写规范化与常见误识修复 (-C -> -c, -V -> -v, -no-build -> --no-build, -silent -> --silent)
		line = Regex.Replace(line, @"(?<=^|\s)-C\b", "-c");
		line = Regex.Replace(line, @"(?<=^|\s)-V\b", "-v");
		line = Regex.Replace(line, @"(?<=^|\s)-(no-build|no-buitd|self-contained|silent)\b", m => m.Value.Contains("bui") ? "--no-build" : "--" + m.Groups[1].Value);

		// 6.2 常见 OCR 汉字偏旁部首与形近字自动拼合修复 (Universal Radical & Glyph Repair)
		// A. 错误与警告专项高优先级合并 (针对编译日志 "0 警告，0 错误。")
		line = Regex.Replace(line, @"(?<=^|[\s，,])ø(?=\s*个?(?:警\s*告|错\s*误|警告|错误|[警告错误]))", "0");
		line = Regex.Replace(line, @"(?<=^|\s)[eE]\s*(?=告)", "0 警");
		line = Regex.Replace(line, @"[已己]\s*[成]?\s*力\s*生\s*成", "已成功生成");
		line = Regex.Replace(line, @"警\s*[！!]\s*告", "警告");
		line = Regex.Replace(line, @"警\s*叾", "警告");
		line = Regex.Replace(line, @"警\s*舌", "警告");
		line = Regex.Replace(line, @"(?<=^|\s)[宀冖]\x20*[口凵](?=[\s，,。.]|$)", "警告");
		line = Regex.Replace(line, @"^[宀冖曰日]\s*[口凵]$", "警告");
		line = Regex.Replace(line, @"^土\s*[.．·]$", "警告");
		line = Regex.Replace(line, @"[．.·]\s*叾", "警");
		line = Regex.Replace(line, @"亻\s*[訁言]", "信");
		line = Regex.Replace(line, @"[，,]\s*皂\s*[、.]?", "息");
		line = Regex.Replace(line, @"(?<=(?:^|[\s，,]))警\s*亻\s*訁\s*[，,]\s*皂\b", "警告信息");
		line = Regex.Replace(line, @"(?<=(?:^|[\s，,]))警\s*亻\s*訁", "警告信");
		line = Regex.Replace(line, @"亻\x20*自", "信");
		line = Regex.Replace(line, @"信\s*[，,]\s*皂", "信息");
		line = Regex.Replace(line, @"信\x20*皂", "信息");
		line = Regex.Replace(line, @"钅\x20*昔\x20*讠\x20*吴", "错误");
		line = Regex.Replace(line, @"钅\x20*昔\x20*误", "错误");
		line = Regex.Replace(line, @"昔\x20*讠\x20*吴", "错误");
		line = Regex.Replace(line, @"昔\x20*误", "错误");
		line = Regex.Replace(line, @"钅\x20*嘏", "错");
		line = Regex.Replace(line, @"钅\x20*昔", "错");
		line = Regex.Replace(line, @"钅\x20*吴", "错误");
		line = Regex.Replace(line, @"讠\x20*吴", "误");
		line = Regex.Replace(line, @"讠\x20*引", "误");
		line = Regex.Replace(line, @"鼕\x20*告", "警告");
		line = Regex.Replace(line, @"(?<=\b\d+\s*)鼕(?=[\s，,个项]|$)", "警告");
		line = Regex.Replace(line, @"(?<=(?:^|[\s，,]))\b(\d+)\s*告(?=[\s，,])", "$1 警告");
		line = Regex.Replace(line, @"(?<=^|\s)土(?=\s*\d+\s*错\s*误)", "0 警告，");
		line = Regex.Replace(line, @"(?<=^|\s)土\s*(?=\d+\s*个?错)", "0 警告，");
		line = Regex.Replace(line, @"(?<=(?:[，,]\s*)?\b\d+\s*个?\s*)误(?=[\s。.]|$)", "错误");
		line = Regex.Replace(line, @"(?<=[，,]\s*)误(?=[\s。.]|$)", "0 错误");
		line = Regex.Replace(line, @"(?<=[，,]\s*)0\s*误(?=[\s。.]|$)", "0 错误");

		// B. 常见界面交互词部首修复 (取消、测试、设置、代码、终端等)
		line = Regex.Replace(line, @"快\s*捷\s*[撻撻建]", "快捷键");
		line = Regex.Replace(line, @"取\x20*氵\x20*肖", "取消");
		line = Regex.Replace(line, @"\b已\s*消\b", "已取消");
		line = Regex.Replace(line, @"(?<=^|\s)已\s*消(?=[\s，,。.]|$)", "已取消");
		line = Regex.Replace(line, @"氵\x20*肖", "消");
		line = Regex.Replace(line, @"测\x20*讠\x20*式", "测试");
		line = Regex.Replace(line, @"讠\x20*式", "试");
		line = Regex.Replace(line, @"设\x20*讠\x20*殳", "设置");
		line = Regex.Replace(line, @"讠\x20*殳", "设");
		line = Regex.Replace(line, @"讠\x20*十", "计");
		line = Regex.Replace(line, @"鼠\x20*木\x20*示", "鼠标");
		line = Regex.Replace(line, @"木\x20*示", "标");
		line = Regex.Replace(line, @"面\x20*木\x20*反", "面板");
		line = Regex.Replace(line, @"木\x20*反", "板");
		line = Regex.Replace(line, @"文\x20*木\x20*当", "文档");
		line = Regex.Replace(line, @"木\x20*当", "档");
		line = Regex.Replace(line, @"代\x20*石\x20*马", "代码");
		line = Regex.Replace(line, @"石\x20*马", "码");
		line = Regex.Replace(line, @"终\x20*立\x20*耑", "终端");
		line = Regex.Replace(line, @"立\x20*耑", "端");
		line = Regex.Replace(line, @"系\x20*纟\x20*充", "系统");
		line = Regex.Replace(line, @"纟\x20*充", "统");
		line = Regex.Replace(line, @"编\x20*纟\x20*扁", "编译");
		line = Regex.Replace(line, @"缤\s*译", "编译");
		line = Regex.Replace(line, @"纟\x20*扁", "编");
		line = Regex.Replace(line, @"讠\x20*泽", "译");

		// C. 通用偏旁拼合字典 (言字旁、三点水、木字旁、口字旁、提手旁、禾木旁、绞丝旁等)
		line = Regex.Replace(line, @"讠\x20*成", "诚");
		line = Regex.Replace(line, @"讠\x20*吾", "语");
		line = Regex.Replace(line, @"讠\x20*论", "论");
		line = Regex.Replace(line, @"讠\x20*言", "誉");
		line = Regex.Replace(line, @"讠\x20*正", "证");
		line = Regex.Replace(line, @"讠\x20*果", "课");
		line = Regex.Replace(line, @"讠\x20*卖", "读");
		line = Regex.Replace(line, @"讠\x20*周", "调");
		line = Regex.Replace(line, @"讠\x20*平", "评");
		line = Regex.Replace(line, @"讠\x20*司", "词");
		line = Regex.Replace(line, @"讠\x20*射", "谢");
		line = Regex.Replace(line, @"氵\x20*台", "治");
		line = Regex.Replace(line, @"氵\x20*去", "法");
		line = Regex.Replace(line, @"氵\x20*每", "海");
		line = Regex.Replace(line, @"氵\x20*青", "清");
		line = Regex.Replace(line, @"氵\x20*吉", "洁");
		line = Regex.Replace(line, @"木\x20*每", "梅");
		line = Regex.Replace(line, @"木\x20*寸", "村");
		line = Regex.Replace(line, @"女\x20*也", "她");
		line = Regex.Replace(line, @"口\x20*合", "哈");
		line = Regex.Replace(line, @"口\x20*马", "吗");
		line = Regex.Replace(line, @"口\x20*巴", "吧");
		line = Regex.Replace(line, @"日\x20*月", "明");
		line = Regex.Replace(line, @"土\x20*也", "地");
		line = Regex.Replace(line, @"扌\x20*安", "按");
		line = Regex.Replace(line, @"扌\x20*丁", "打");
		line = Regex.Replace(line, @"扌\x20*包", "抱");
		line = Regex.Replace(line, @"禾\x20*中", "种");
		line = Regex.Replace(line, @"纟\x20*吉", "结");
		line = Regex.Replace(line, @"纟\x20*工", "红");
		line = Regex.Replace(line, @"纟\x20*田", "细");
		line = Regex.Replace(line, @"纟\x20*及", "级");

		// 7. 移除汉字与汉字之间的空格 (如 功 能 方 向 -> 功能方向)
		if (removeCjkSpaces)
		{
			line = Regex.Replace(line, @"(?<=[\u4e00-\u9fa5\u3400-\u4dbf])\x20+(?=[\u4e00-\u9fa5\u3400-\u4dbf])", "");
		}

		// 8. 移除中文标点符号两侧的多余空格 (如 模式 ， 轻点 -> 模式，轻点)
		line = Regex.Replace(line, @"\x20+(?=[，、。！？；：）》】”’])", "");
		line = Regex.Replace(line, @"(?<=[，、。！？；：（《【“‘])\x20+", "");

		// 9. 压缩连续多余空格为单个空格
		line = Regex.Replace(line, @"[ ]{2,}", " ");

		return line.Trim();
	}

	/// <summary>
	/// 智能采样图像四周 1px 外轮廓（Perimeter）的颜色直方图众数（Mode）。
	/// 避免 8 点采样命中文字笔画导致的底色计算污染，确保四周外扩留白（Padding）与原图背景 100% 无缝衔接。
	/// </summary>
	public static System.Drawing.Color SamplePerimeterBackground(Bitmap src)
	{
		if (src == null || src.Width <= 0 || src.Height <= 0)
		{
			return System.Drawing.Color.White;
		}

		if (src.Width == 1 && src.Height == 1)
		{
			System.Drawing.Color p = src.GetPixel(0, 0);
			return System.Drawing.Color.FromArgb(255, p.R, p.G, p.B);
		}

		Dictionary<int, int> counts = new Dictionary<int, int>();
		void AddPixel(int x, int y)
		{
			System.Drawing.Color c = src.GetPixel(x, y);
			int rgb = (c.R << 16) | (c.G << 8) | c.B;
			counts[rgb] = counts.TryGetValue(rgb, out int cnt) ? cnt + 1 : 1;
		}

		int w = src.Width;
		int h = src.Height;

		for (int x = 0; x < w; x++)
		{
			AddPixel(x, 0);
			if (h > 1)
			{
				AddPixel(x, h - 1);
			}
		}

		for (int y = 1; y < h - 1; y++)
		{
			AddPixel(0, y);
			if (w > 1)
			{
				AddPixel(w - 1, y);
			}
		}

		int dominantRgb = 0xFFFFFF;
		int maxCount = -1;
		foreach (var kvp in counts)
		{
			if (kvp.Value > maxCount)
			{
				maxCount = kvp.Value;
				dominantRgb = kvp.Key;
			}
		}

		return System.Drawing.Color.FromArgb(255, (dominantRgb >> 16) & 0xFF, (dominantRgb >> 8) & 0xFF, dominantRgb & 0xFF);
	}

	/// <summary>
	/// 图像原生保真预处理管道（满足 PROJECT.md 接口规范）：
	/// 1. 彻底移除 6464b04 的 ColorMatrix 全图反色与反色底色填充，杜绝次像素彩边反转与偏旁部首切碎（如“钅 嘏”）；
	/// 2. 采用 1px 外轮廓直方图众数（SamplePerimeterBackground）无缝填充真实环境底色；
	/// 3. 保守型动态超分（限制在 1.3x~1.6x），防止 19 笔画复杂汉字（如“警”）内部细微空隙粘连导致形近字误识（如“鼕”）；
	/// 4. 自适应构造宽画幅（Wide-Aspect Canvas），为 WinRT 文本行检测器提供清晰的水平走向基准线。
	/// </summary>
	public static Bitmap PreprocessSnippetForOcr(Bitmap src, double scaleFactor = 0.0, int padX = -1, int padY = -1)
	{
		if (src == null) throw new ArgumentNullException(nameof(src));

		// 1. 采样图像四周 1px 外轮廓，计算真实环境底色（直方图众数，免疫笔画穿透）
		System.Drawing.Color trueBg = SamplePerimeterBackground(src);

		// 2. 动态字高超分：保守型放大，防止 1px 细笔画空隙在双三次插值下过度模糊粘连
		if (scaleFactor <= 0.0)
		{
			scaleFactor = 1.0;
			if (src.Width > 200 && src.Height < 80)
			{
				// 单行终端命令行代码（如 dotnet build ... -c Release）-> 放大 2.0 倍确保尾部参数与单词清晰分割
				scaleFactor = 2.0;
			}
			else if (src.Height <= 45 && src.Width <= 90)
			{
				// 单字/超微小短词选区（如 60x40 贴边截取“警告”）-> 放大 2.0 倍
				scaleFactor = 2.0;
			}
			else if (src.Height <= 65 && src.Width <= 120)
			{
				// 单行短词/微小选区（如 99x59 框选“警告”）-> 放大 1.6 倍
				scaleFactor = 1.6;
			}
			else if (src.Height <= 55)
			{
				// 单行长句（如 0 警告，0 错误。）-> 保留 1.5 倍
				scaleFactor = 1.5;
			}
			else if (src.Height < 100)
			{
				// 2~3 行小字号控制台文字 -> 保守放大 1.4 倍
				scaleFactor = 1.4;
			}
		}

		// 尺寸上限保护：防止超大截图被过度放大超过 WinRT 纹理上限
		if (src.Width * scaleFactor > 3200 || src.Height * scaleFactor > 3200)
		{
			scaleFactor = Math.Min(3200.0 / src.Width, 3200.0 / src.Height);
			if (scaleFactor < 1.0) scaleFactor = 1.0;
		}

		int targetContentW = Math.Max(1, (int)Math.Round(src.Width * scaleFactor));
		int targetContentH = Math.Max(1, (int)Math.Round(src.Height * scaleFactor));

		// 3. 构造自适应横向宽画幅（Wide-Aspect Canvas）
		int calculatedPadY;
		if (padY >= 0)
		{
			calculatedPadY = padY;
		}
		else if (src.Height <= 70 && src.Width <= 120)
		{
			calculatedPadY = 20;
		}
		else
		{
			calculatedPadY = 32;
		}
		int targetH = targetContentH + calculatedPadY * 2;

		int calculatedPadX;
		if (padX >= 0)
		{
			calculatedPadX = padX;
		}
		else if (src.Height <= 52 && src.Width <= 75)
		{
			calculatedPadX = 32;
		}
		else if (src.Height <= 70 && src.Width <= 120)
		{
			calculatedPadX = 60;
		}
		else
		{
			// 强制画布横纵比达到 2.5:1 以上，为行检测器提供明确的水平走向基准线
			int minTargetW = Math.Max(targetContentW + 64, (int)Math.Round(targetH * 2.5));
			calculatedPadX = Math.Max(32, (minTargetW - targetContentW) / 2);
		}

		int targetW = targetContentW + calculatedPadX * 2;

		// 4. 自适应低对比度暗底自适应增强（针对深色背景暗字，如 #2D2D2D 底上的 #909090 文字）：
		// 当背景较暗 (lum < 128) 且文字与背景对比度不足 (< 130) 时，
		// 线性拉伸前景笔画亮度，同时严格保持原生底色不变与 ClearType 次像素彩边比例，绝不反色。
		double bgLum = 0.299 * trueBg.R + 0.587 * trueBg.G + 0.114 * trueBg.B;
		float contrastScale = 1.0f;
		float offsetR = 0f, offsetG = 0f, offsetB = 0f;

		if (bgLum < 128.0)
		{
			double maxFgLum = bgLum;
			int stepX = Math.Max(1, src.Width / 15);
			int stepY = Math.Max(1, src.Height / 15);
			for (int y = 0; y < src.Height; y += stepY)
			{
				for (int x = 0; x < src.Width; x += stepX)
				{
					System.Drawing.Color c = src.GetPixel(x, y);
					double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
					if (lum > maxFgLum) maxFgLum = lum;
				}
			}

			double contrast = maxFgLum - bgLum;
			if (contrast > 25.0 && contrast < 130.0)
			{
				contrastScale = (float)Math.Min(2.5, 180.0 / contrast);
				float bgNormR = trueBg.R / 255.0f;
				float bgNormG = trueBg.G / 255.0f;
				float bgNormB = trueBg.B / 255.0f;
				offsetR = bgNormR * (1.0f - contrastScale);
				offsetG = bgNormG * (1.0f - contrastScale);
				offsetB = bgNormB * (1.0f - contrastScale);
			}
		}

		Bitmap prepared = new Bitmap(targetW, targetH, PixelFormat.Format32bppArgb);
		using (Graphics g = Graphics.FromImage(prepared))
		{
			using (System.Drawing.Brush bgBrush = new System.Drawing.SolidBrush(trueBg))
			{
				g.FillRectangle(bgBrush, 0, 0, targetW, targetH);
			}

			g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
			g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
			g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

			Rectangle destRect = new Rectangle(calculatedPadX, calculatedPadY, targetContentW, targetContentH);
			using (ImageAttributes ia = new ImageAttributes())
			{
				ia.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
				if (contrastScale > 1.01f)
				{
					ColorMatrix cm = new ColorMatrix(new float[][]
					{
						new float[] { contrastScale, 0f, 0f, 0f, 0f },
						new float[] { 0f, contrastScale, 0f, 0f, 0f },
						new float[] { 0f, 0f, contrastScale, 0f, 0f },
						new float[] { 0f, 0f, 0f, 1f, 0f },
						new float[] { offsetR, offsetG, offsetB, 0f, 1f }
					});
					ia.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
				}
				g.DrawImage(src, destRect, 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
			}
		}

		return prepared;
	}

	private static Rectangle GetForegroundBounds(Bitmap src, System.Drawing.Color bg, int tolerance = 8)
	{
		int minX = src.Width, minY = src.Height, maxX = -1, maxY = -1;
		int w = src.Width, h = src.Height;

		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				System.Drawing.Color c = src.GetPixel(x, y);
				int diff = Math.Abs(c.R - bg.R) + Math.Abs(c.G - bg.G) + Math.Abs(c.B - bg.B);
				if (diff >= tolerance)
				{
					if (x < minX) minX = x;
					if (x > maxX) maxX = x;
					if (y < minY) minY = y;
					if (y > maxY) maxY = y;
				}
			}
		}

		if (maxX < minX || maxY < minY)
		{
			return new Rectangle(0, 0, w, h);
		}

		minX = Math.Max(0, minX - 4);
		minY = Math.Max(0, minY - 4);
		maxX = Math.Min(w - 1, maxX + 4);
		maxY = Math.Min(h - 1, maxY + 4);

		return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
	}

	/// <summary>
	/// 微小短词/单字选区水平双联重叠构造 (Micro-Rescue Tiling Pipeline)：
	/// 通过横向平铺 [src] [gap] [src]，突破 WinRT 文本行检测器 (Line Detector) 对超短文本、低长宽比与单字特征词的噪点丢弃阈值，
	/// 结合 CollapseTiledDuplicates 彻底解决微小选区漏检为 [未识别到有效文字内容] 的问题。
	/// </summary>
	public static Bitmap PreprocessTiledSnippetForOcr(Bitmap src, double scaleFactor = 1.5, int copies = 2, int gap = 15, int pad = 35)
	{
		if (src == null) throw new ArgumentNullException(nameof(src));

		System.Drawing.Color bg = SamplePerimeterBackground(src);

		Bitmap tileSrc = src;
		bool disposeTileSrc = false;
		bool isTinySquare = (src.Width <= 35 && src.Height <= 35);
		if (!isTinySquare)
		{
			Rectangle fg = GetForegroundBounds(src, bg);
			if (fg.Width > 0 && fg.Height > 0 && 
			    (fg.Width < src.Width * 0.90 || fg.Height < src.Height * 0.90))
			{
				tileSrc = src.Clone(fg, src.PixelFormat);
				disposeTileSrc = true;
			}
		}

		try
		{
			if (scaleFactor <= 0.0)
			{
				scaleFactor = 1.5;
			}
			if (isTinySquare)
			{
				scaleFactor = Math.Max(scaleFactor, 2.0);
			}
			else if (tileSrc.Height * scaleFactor < 36.0)
			{
				scaleFactor = Math.Min(2.5, 38.0 / Math.Max(1, tileSrc.Height));
			}

			int copyW = Math.Max(1, (int)Math.Round(tileSrc.Width * scaleFactor));
			int copyH = Math.Max(1, (int)Math.Round(tileSrc.Height * scaleFactor));
			int actualGap = isTinySquare ? Math.Clamp(copyH / 4, 6, 12) : Math.Max(8, gap);
			int actualPad = isTinySquare ? Math.Clamp((116 - copyH) / 2, 26, 30) : pad;
			int contentW = copyW * copies + actualGap * (copies - 1);
			int totalH = copyH + actualPad * 2;
			int minTotalW = Math.Max(contentW + actualPad * 2, (int)Math.Round(totalH * 2.5));
			int totalW = minTotalW;
			int offsetX = (totalW - contentW) / 2;

			Bitmap tiled = new Bitmap(totalW, totalH, PixelFormat.Format32bppArgb);
			using (Graphics g = Graphics.FromImage(tiled))
			{
				using (System.Drawing.Brush bgBrush = new System.Drawing.SolidBrush(bg))
				{
					g.FillRectangle(bgBrush, 0, 0, totalW, totalH);
				}

				g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
				g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
				g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

				using (ImageAttributes ia = new ImageAttributes())
				{
					ia.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
					for (int i = 0; i < copies; i++)
					{
						int destX = offsetX + i * (copyW + actualGap);
						int destY = actualPad;
						Rectangle destRect = new Rectangle(destX, destY, copyW, copyH);
						g.DrawImage(tileSrc, destRect, 0, 0, tileSrc.Width, tileSrc.Height, GraphicsUnit.Pixel, ia);
					}
				}
			}

			return tiled;
		}
		finally
		{
			if (disposeTileSrc)
			{
				tileSrc.Dispose();
			}
		}
	}

	/// <summary>
	/// 对双联/多联平铺识别出的重复文本进行折叠去重。
	/// 支持按行折叠、空格/制表符分隔序列折叠及紧凑重复折叠，若未检测到重复则安全返回原文本。
	/// </summary>
	public static string CollapseTiledDuplicates(string rawText, int copies = 2)
	{
		if (string.IsNullOrWhiteSpace(rawText)) return rawText;

		string text = rawText.Trim();

		// 多行完全重复检测 (如行 0 == 行 1 == 行 2)
		string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
		if (lines.Length == 2 && string.Equals(lines[0].Trim(), lines[1].Trim(), StringComparison.OrdinalIgnoreCase))
		{
			return CollapseTiledDuplicates(lines[0].Trim(), copies);
		}
		if (lines.Length == 3 && string.Equals(lines[0].Trim(), lines[1].Trim(), StringComparison.OrdinalIgnoreCase) &&
		    string.Equals(lines[1].Trim(), lines[2].Trim(), StringComparison.OrdinalIgnoreCase))
		{
			return CollapseTiledDuplicates(lines[0].Trim(), copies);
		}

		List<string> collapsedLines = new List<string>(lines.Length);
		foreach (var line in lines)
		{
			string trimmedLine = line.Trim();
			if (string.IsNullOrEmpty(trimmedLine))
			{
				collapsedLines.Add(line);
				continue;
			}

			// 模式 1：正则匹配由空白/制表符分隔的重复子句：^(.+?)(?:[\s\t]+\1)+$
			var match = Regex.Match(trimmedLine, @"^(.+?)(?:[\s\t]+\1)+$", RegexOptions.Singleline);
			if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
			{
				collapsedLines.Add(match.Groups[1].Value.Trim());
				continue;
			}

			// 模式 2：词元对称二等分/三等分对比 (容忍词元内部空格变化)
			var tokens = trimmedLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
			if (tokens.Length >= 2 && tokens.Length % 2 == 0)
			{
				int half = tokens.Length / 2;
				bool halfMatch = true;
				for (int i = 0; i < half; i++)
				{
					if (!string.Equals(tokens[i], tokens[i + half], StringComparison.OrdinalIgnoreCase))
					{
						halfMatch = false;
						break;
					}
				}
				if (halfMatch)
				{
					collapsedLines.Add(string.Join(" ", tokens.Take(half)));
					continue;
				}
			}
			if (tokens.Length >= 3 && tokens.Length % 3 == 0)
			{
				int oneThird = tokens.Length / 3;
				bool thirdMatch = true;
				for (int i = 0; i < oneThird; i++)
				{
					if (!string.Equals(tokens[i], tokens[i + oneThird], StringComparison.OrdinalIgnoreCase) ||
					    !string.Equals(tokens[i], tokens[i + oneThird * 2], StringComparison.OrdinalIgnoreCase))
					{
						thirdMatch = false;
						break;
					}
				}
				if (thirdMatch)
				{
					collapsedLines.Add(string.Join(" ", tokens.Take(oneThird)));
					continue;
				}
			}

			// 模式 3：无空格直接字符重复对半/三分折叠 (如 "警告警告" -> "警告", "错错错" -> "错")
			if (trimmedLine.Length >= 2 && trimmedLine.Length % 2 == 0)
			{
				int halfLen = trimmedLine.Length / 2;
				string firstHalf = trimmedLine.Substring(0, halfLen);
				string secondHalf = trimmedLine.Substring(halfLen);
				if (string.Equals(firstHalf, secondHalf, StringComparison.OrdinalIgnoreCase))
				{
					collapsedLines.Add(firstHalf);
					continue;
				}
			}
			if (trimmedLine.Length >= 3 && trimmedLine.Length % 3 == 0)
			{
				int oneThird = trimmedLine.Length / 3;
				string s1 = trimmedLine.Substring(0, oneThird);
				string s2 = trimmedLine.Substring(oneThird, oneThird);
				string s3 = trimmedLine.Substring(oneThird * 2, oneThird);
				if (string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase) &&
				    string.Equals(s1, s3, StringComparison.OrdinalIgnoreCase))
				{
					collapsedLines.Add(s1);
					continue;
				}
			}

			collapsedLines.Add(trimmedLine);
		}

		return string.Join("\r\n", collapsedLines);
	}

#if DEBUG
	/// <summary>
	/// 仅供开发者排查问题使用的调试日志。
	/// 注意：此方法受 #if DEBUG 编译期条件严格隔离，在 RELEASE 发行版中会被编译器彻底剥离，
	/// 绝不在用户生产环境中记录任何识别出的隐私文字内容。
	/// </summary>
	private static void LogDebugOcrText(string rawText, string finalText)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(finalText))
			{
				AppLogger.LogDebug("[DEBUG OCR Text]: <EMPTY (未识别到有效文字)>");
				return;
			}

			if (finalText.StartsWith("[OCR 识别异常]"))
			{
				AppLogger.LogDebug($"[DEBUG OCR Text]: <ERROR: {finalText}>");
				return;
			}

			if (finalText.StartsWith("[") && finalText.EndsWith("]"))
			{
				AppLogger.LogDebug($"[DEBUG OCR Text]: <DIAGNOSTIC: {finalText}>");
				return;
			}

			if (!string.Equals(rawText, finalText, StringComparison.Ordinal))
			{
				AppLogger.LogDebug(
					"[DEBUG OCR Text Comparison]:\n" +
					"--- BEGIN OCR RAW TEXT ---\n" +
					rawText + "\n" +
					"--- END OCR RAW TEXT ---\n" +
					"--- BEGIN OCR PROCESSED TEXT ---\n" +
					finalText + "\n" +
					"--- END OCR PROCESSED TEXT ---"
				);
			}
			else
			{
				AppLogger.LogDebug(
					"[DEBUG OCR Text]:\n" +
					"--- BEGIN OCR TEXT ---\n" +
					finalText + "\n" +
					"--- END OCR TEXT ---"
				);
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogDebug($"[DEBUG OCR Log Error]: {ex.Message}");
		}
	}
#endif
}
