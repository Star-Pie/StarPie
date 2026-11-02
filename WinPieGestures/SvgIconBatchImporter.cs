using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;

namespace WinPieGestures;

public enum SvgIconBatchItemStatus
{
	Success,
	Skipped,
	Failure
}

public sealed class SvgIconBatchItemResult
{
	public string SourcePath { get; }
	public SvgIconBatchItemStatus Status { get; }
	public string ReasonCode { get; }
	public string? TargetPath { get; }

	public SvgIconBatchItemResult(string sourcePath, SvgIconBatchItemStatus status, string reasonCode, string? targetPath = null)
	{
		SourcePath = sourcePath;
		Status = status;
		ReasonCode = reasonCode;
		TargetPath = targetPath;
	}
}

public sealed class SvgIconBatchImportResult
{
	public string TargetDirectory { get; }
	public IReadOnlyList<SvgIconBatchItemResult> Items { get; }
	public bool CacheInvalidated { get; }
	public bool Cancelled { get; }
	public string? BatchReasonCode { get; }

	public int SuccessCount => Items.Count(item => item.Status == SvgIconBatchItemStatus.Success);
	public int SkippedCount => Items.Count(item => item.Status == SvgIconBatchItemStatus.Skipped);
	public int FailureCount => Items.Count(item => item.Status == SvgIconBatchItemStatus.Failure);

	internal SvgIconBatchImportResult(
		string targetDirectory,
		IEnumerable<SvgIconBatchItemResult> items,
		bool cacheInvalidated,
		bool cancelled,
		string? batchReasonCode)
	{
		TargetDirectory = targetDirectory;
		Items = items.ToList().AsReadOnly();
		CacheInvalidated = cacheInvalidated;
		Cancelled = cancelled;
		BatchReasonCode = batchReasonCode;
	}
}

public static class SvgIconReasonCodes
{
	public const string Success = "SUCCESS";
	public const string UnknownExtension = "UNKNOWN_EXTENSION";
	public const string DuplicateSource = "DUPLICATE_SOURCE";
	public const string InvalidSourcePath = "INVALID_SOURCE_PATH";
	public const string FileNotFound = "FILE_NOT_FOUND";
	public const string FileTooLarge = "FILE_TOO_LARGE";
	public const string InvalidXml = "INVALID_XML";
	public const string DtdForbidden = "DTD_FORBIDDEN";
	public const string InvalidRoot = "INVALID_ROOT";
	public const string UnsupportedNamespace = "UNSUPPORTED_NAMESPACE";
	public const string UnsupportedElement = "UNSUPPORTED_ELEMENT";
	public const string UnsupportedAttribute = "UNSUPPORTED_ATTRIBUTE";
	public const string TransformUnsupported = "TRANSFORM_UNSUPPORTED";
	public const string StrokeUnsupported = "STROKE_UNSUPPORTED";
	public const string StyleUnsupported = "STYLE_UNSUPPORTED";
	public const string GradientUnsupported = "GRADIENT_UNSUPPORTED";
	public const string ExternalReference = "EXTERNAL_REFERENCE";
	public const string PaintTransparent = "PAINT_TRANSPARENT";
	public const string PaintUnsupported = "PAINT_UNSUPPORTED";
	public const string MultiColor = "MULTI_COLOR";
	public const string InvisiblePath = "INVISIBLE_PATH";
	public const string PathDataMissing = "PATH_DATA_MISSING";
	public const string InvalidPathData = "INVALID_PATH_DATA";
	public const string NonFiniteCoordinate = "NON_FINITE_COORDINATE";
	public const string EmptyGeometry = "EMPTY_GEOMETRY";
	public const string UnsupportedText = "UNSUPPORTED_TEXT";
	public const string TargetDirectoryInvalid = "TARGET_DIRECTORY_INVALID";
	public const string SourceDirectoryInvalid = "SOURCE_DIRECTORY_INVALID";
	public const string SourceDestinationCollision = "SOURCE_DESTINATION_COLLISION";
	public const string IoError = "IO_ERROR";
	public const string Cancelled = "CANCELLED";
	public const string CacheInvalidationFailed = "CACHE_INVALIDATION_FAILED";
}

public static class SvgIconBatchImporter
{
	public const long MaxSvgFileBytes = 2L * 1024L * 1024L;

	private static readonly Regex NumericTokenRegex = new(
		@"[-+]?(?:(?:\d+\.\d*)|(?:\.\d+)|(?:\d+))(?:[eE][-+]?\d+)?",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly UTF8Encoding Utf8WithoutBom = new(false);
	private static readonly XNamespace SvgNamespace = "http://www.w3.org/2000/svg";

	public static SvgIconBatchImportResult ImportFolder(
		string folderPath,
		string? targetDirectory = null,
		Action? invalidateCache = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
		{
			return new SvgIconBatchImportResult(
				targetDirectory ?? "",
				Array.Empty<SvgIconBatchItemResult>(),
				false,
				false,
				SvgIconReasonCodes.SourceDirectoryInvalid);
		}

		string[] files;
		try
		{
			files = Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
				.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
				.ToArray();
		}
		catch
		{
			return new SvgIconBatchImportResult(
				targetDirectory ?? "",
				Array.Empty<SvgIconBatchItemResult>(),
				false,
				false,
				SvgIconReasonCodes.SourceDirectoryInvalid);
		}

		return ImportFiles(files, targetDirectory, invalidateCache, cancellationToken);
	}

	public static SvgIconBatchImportResult ImportFiles(
		IEnumerable<string> sourcePaths,
		string? targetDirectory = null,
		Action? invalidateCache = null,
		CancellationToken cancellationToken = default)
	{
		string target;
		try
		{
			target = targetDirectory ?? IconHelper.GetCustomIconsDirectory();
			Directory.CreateDirectory(target);
		}
		catch
		{
			return new SvgIconBatchImportResult(
				targetDirectory ?? "",
				Array.Empty<SvgIconBatchItemResult>(),
				false,
				false,
				SvgIconReasonCodes.TargetDirectoryInvalid);
		}

		var items = new List<SvgIconBatchItemResult>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var created = new List<string>();

		try
		{
			foreach (string? rawPath in sourcePaths ?? Array.Empty<string>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				string sourcePath = rawPath ?? "";
				string fullPath;
				try
				{
					fullPath = Path.GetFullPath(sourcePath);
				}
				catch
				{
					items.Add(Failure(sourcePath, SvgIconReasonCodes.InvalidSourcePath));
					continue;
				}

				if (!seen.Add(fullPath))
				{
					items.Add(Skipped(fullPath, SvgIconReasonCodes.DuplicateSource));
					continue;
				}

				string extension = Path.GetExtension(fullPath);
				if (!string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase))
				{
					items.Add(Skipped(fullPath, SvgIconReasonCodes.UnknownExtension));
					continue;
				}

				if (!File.Exists(fullPath))
				{
					items.Add(Failure(fullPath, SvgIconReasonCodes.FileNotFound));
					continue;
				}

				SvgPathValidationResult validation = ValidateSvgFile(fullPath, cancellationToken);
				if (!validation.IsSuccess)
				{
					items.Add(Failure(fullPath, validation.ReasonCode));
					continue;
				}

				if (!TryWriteCanonicalPath(
					target,
					fullPath,
					Path.GetFileNameWithoutExtension(fullPath),
					validation.PathData,
					created,
					out string? targetPath,
					out string reasonCode))
				{
					items.Add(Failure(fullPath, reasonCode));
					continue;
				}

				items.Add(new SvgIconBatchItemResult(
					fullPath,
					SvgIconBatchItemStatus.Success,
					SvgIconReasonCodes.Success,
					targetPath));
			}
		}
		catch (OperationCanceledException)
		{
			RollbackCreatedFiles(created);
			for (int index = 0; index < items.Count; index++)
			{
				if (items[index].Status == SvgIconBatchItemStatus.Success)
				{
					items[index] = Failure(items[index].SourcePath, SvgIconReasonCodes.Cancelled);
				}
			}
			return new SvgIconBatchImportResult(target, items, false, true, SvgIconReasonCodes.Cancelled);
		}

		bool cacheInvalidated = false;
		string? batchReasonCode = null;
		if (items.Any(item => item.Status == SvgIconBatchItemStatus.Success))
		{
			try
			{
				(invalidateCache ?? IconHelper.InvalidateCustomIconCache)();
				cacheInvalidated = true;
			}
			catch
			{
				batchReasonCode = SvgIconReasonCodes.CacheInvalidationFailed;
			}
		}

		return new SvgIconBatchImportResult(target, items, cacheInvalidated, false, batchReasonCode);
	}

	public static SvgIconBatchImportResult ImportBarePath(
		string pathData,
		string? iconName = null,
		string? targetDirectory = null,
		Action? invalidateCache = null,
		CancellationToken cancellationToken = default)
	{
		string target;
		try
		{
			target = targetDirectory ?? IconHelper.GetCustomIconsDirectory();
			Directory.CreateDirectory(target);
		}
		catch
		{
			return new SvgIconBatchImportResult(
				targetDirectory ?? "",
				Array.Empty<SvgIconBatchItemResult>(),
				false,
				false,
				SvgIconReasonCodes.TargetDirectoryInvalid);
		}

		SvgPathValidationResult validation = ValidatePathData(pathData);
		if (!validation.IsSuccess)
		{
			return new SvgIconBatchImportResult(
				target,
				new[] { Failure(iconName ?? "Vector", validation.ReasonCode) },
				false,
				false,
				null);
		}

		if (Encoding.UTF8.GetByteCount(validation.PathData) > MaxSvgFileBytes)
		{
			return new SvgIconBatchImportResult(
				target,
				new[] { Failure(iconName ?? "Vector", SvgIconReasonCodes.FileTooLarge) },
				false,
				false,
				null);
		}

		var created = new List<string>();
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!TryWriteCanonicalPath(
				target,
				null,
				iconName ?? "Vector",
				validation.PathData,
				created,
				out string? targetPath,
				out string reasonCode))
			{
				return new SvgIconBatchImportResult(
					target,
					new[] { Failure(iconName ?? "Vector", reasonCode) },
					false,
					false,
					null);
			}

			bool cacheInvalidated = false;
			string? batchReasonCode = null;
			try
			{
				(invalidateCache ?? IconHelper.InvalidateCustomIconCache)();
				cacheInvalidated = true;
			}
			catch
			{
				batchReasonCode = SvgIconReasonCodes.CacheInvalidationFailed;
			}

			return new SvgIconBatchImportResult(
				target,
				new[]
				{
					new SvgIconBatchItemResult(
						iconName ?? "Vector",
						SvgIconBatchItemStatus.Success,
						SvgIconReasonCodes.Success,
						targetPath)
				},
				cacheInvalidated,
				false,
				batchReasonCode);
		}
		catch (OperationCanceledException)
		{
			RollbackCreatedFiles(created);
			return new SvgIconBatchImportResult(
				target,
				Array.Empty<SvgIconBatchItemResult>(),
				false,
				true,
				SvgIconReasonCodes.Cancelled);
		}
	}

	private static SvgPathValidationResult ValidateSvgFile(string sourcePath, CancellationToken cancellationToken)
	{
		try
		{
			var fileInfo = new FileInfo(sourcePath);
			if (fileInfo.Length > MaxSvgFileBytes)
			{
				return SvgPathValidationResult.Failure(SvgIconReasonCodes.FileTooLarge);
			}

			var settings = new XmlReaderSettings
			{
				DtdProcessing = DtdProcessing.Prohibit,
				XmlResolver = null,
				IgnoreComments = true,
				IgnoreProcessingInstructions = true,
				IgnoreWhitespace = true,
				MaxCharactersFromEntities = 0,
				MaxCharactersInDocument = MaxSvgFileBytes
			};

			using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
			using var reader = XmlReader.Create(stream, settings);
			cancellationToken.ThrowIfCancellationRequested();
			XDocument document = XDocument.Load(reader, LoadOptions.None);
			cancellationToken.ThrowIfCancellationRequested();
			return ValidateDocument(document);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (XmlException ex)
		{
			return SvgPathValidationResult.Failure(
				ex.Message.Contains("DTD", StringComparison.OrdinalIgnoreCase) ||
				ex.Message.Contains("DOCTYPE", StringComparison.OrdinalIgnoreCase)
					? SvgIconReasonCodes.DtdForbidden
					: SvgIconReasonCodes.InvalidXml);
		}
		catch
		{
			return SvgPathValidationResult.Failure(SvgIconReasonCodes.IoError);
		}
	}

	private static SvgPathValidationResult ValidateDocument(XDocument document)
	{
		XElement? root = document.Root;
		if (root == null || root.Name.LocalName != "svg" || !IsAllowedNamespace(root.Name.Namespace))
		{
			return SvgPathValidationResult.Failure(SvgIconReasonCodes.InvalidRoot);
		}

		var state = new DocumentValidationState();
		string? reason = ValidateElement(root, "currentColor", true, state);
		if (reason != null)
		{
			return SvgPathValidationResult.Failure(reason);
		}
		if (state.Paths.Count == 0)
		{
			return SvgPathValidationResult.Failure(SvgIconReasonCodes.PathDataMissing);
		}
		if (state.Fills.Count > 1)
		{
			return SvgPathValidationResult.Failure(SvgIconReasonCodes.MultiColor);
		}

		string combined = string.Join(" ", state.Paths);
		SvgPathValidationResult combinedValidation = ValidatePathData(combined);
		return combinedValidation;
	}

	private static string? ValidateElement(
		XElement element,
		string inheritedFill,
		bool isRoot,
		DocumentValidationState state)
	{
		if (!IsAllowedNamespace(element.Name.Namespace))
		{
			return SvgIconReasonCodes.UnsupportedNamespace;
		}

		switch (element.Name.LocalName)
		{
			case "svg":
				if (!isRoot)
				{
					return SvgIconReasonCodes.UnsupportedElement;
				}
				return ValidateContainer(element, inheritedFill, true, state);
			case "g":
				return ValidateContainer(element, inheritedFill, false, state);
			case "path":
				return ValidatePathElement(element, inheritedFill, state);
			case "title":
			case "desc":
				return ValidateHarmlessMetadataElement(element);
			case "metadata":
				return null;
			case "use":
			case "image":
			case "script":
			case "foreignObject":
				return SvgIconReasonCodes.ExternalReference;
			case "linearGradient":
			case "radialGradient":
			case "pattern":
				return SvgIconReasonCodes.GradientUnsupported;
			default:
				return SvgIconReasonCodes.UnsupportedElement;
		}
	}

	private static string? ValidateContainer(
		XElement element,
		string inheritedFill,
		bool isRoot,
		DocumentValidationState state)
	{
		var allowed = isRoot
			? new HashSet<string>(StringComparer.Ordinal)
			{
				"viewBox", "width", "height", "version", "preserveAspectRatio", "id", "fill"
			}
			: new HashSet<string>(StringComparer.Ordinal) { "id", "fill" };

		string effectiveFill = inheritedFill;
		string? reason = ValidateAttributes(element, allowed, ref effectiveFill);
		if (reason != null)
		{
			return reason;
		}
		if (HasNonWhitespaceText(element))
		{
			return SvgIconReasonCodes.UnsupportedText;
		}

		foreach (XElement child in element.Elements())
		{
			reason = ValidateElement(child, effectiveFill, false, state);
			if (reason != null)
			{
				return reason;
			}
		}
		return null;
	}

	private static string? ValidatePathElement(
		XElement element,
		string inheritedFill,
		DocumentValidationState state)
	{
		var allowed = new HashSet<string>(StringComparer.Ordinal) { "id", "d", "fill" };
		string effectiveFill = inheritedFill;
		string? reason = ValidateAttributes(element, allowed, ref effectiveFill);
		if (reason != null)
		{
			return reason;
		}
		if (HasNonWhitespaceText(element))
		{
			return SvgIconReasonCodes.UnsupportedText;
		}

		string? pathData = element.Attribute("d")?.Value;
		if (string.IsNullOrWhiteSpace(pathData))
		{
			return SvgIconReasonCodes.PathDataMissing;
		}
		if (string.Equals(effectiveFill, "none", StringComparison.OrdinalIgnoreCase))
		{
			return SvgIconReasonCodes.InvisiblePath;
		}

		SvgPathValidationResult validation = ValidatePathData(pathData);
		if (!validation.IsSuccess)
		{
			return validation.ReasonCode;
		}

		state.Fills.Add(effectiveFill);
		state.Paths.Add(validation.PathData);
		return null;
	}

	private static string? ValidateHarmlessMetadataElement(XElement element)
	{
		var allowed = new HashSet<string>(StringComparer.Ordinal) { "id" };
		string inheritedFill = "currentColor";
		return ValidateAttributes(element, allowed, ref inheritedFill);
	}

	private static string? ValidateAttributes(
		XElement element,
		HashSet<string> allowed,
		ref string effectiveFill)
	{
			foreach (XAttribute attribute in element.Attributes())
		{
			if (attribute.IsNamespaceDeclaration)
			{
				continue;
			}

			string name = attribute.Name.LocalName;
			if (name == "transform")
			{
				return SvgIconReasonCodes.TransformUnsupported;
			}
			if (name == "stroke" || name.StartsWith("stroke-", StringComparison.Ordinal))
			{
				return SvgIconReasonCodes.StrokeUnsupported;
			}
			if (name == "style")
			{
				return SvgIconReasonCodes.StyleUnsupported;
			}
			if (name == "href")
			{
				return SvgIconReasonCodes.ExternalReference;
			}
			if (attribute.Name.Namespace != XNamespace.None)
			{
				return SvgIconReasonCodes.UnsupportedAttribute;
			}
			if (name == "fill" && allowed.Contains("fill"))
			{
				string value = attribute.Value.Trim();
				if (value.Length == 0)
				{
					return SvgIconReasonCodes.UnsupportedAttribute;
				}
				if (value.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
				{
					return SvgIconReasonCodes.GradientUnsupported;
				}
				string? paintReason = NormalizePaint(value, out string normalizedPaint);
				if (paintReason != null)
				{
					return paintReason;
				}
				effectiveFill = normalizedPaint;
				continue;
			}
			if (!allowed.Contains(name))
			{
				return SvgIconReasonCodes.UnsupportedAttribute;
			}
		}
		return null;
	}

	private static string? NormalizePaint(string value, out string normalizedPaint)
	{
		normalizedPaint = "";
		string paint = value.Trim();
		if (string.Equals(paint, "currentColor", StringComparison.OrdinalIgnoreCase))
		{
			normalizedPaint = "currentColor";
			return null;
		}
		if (string.Equals(paint, "none", StringComparison.OrdinalIgnoreCase))
		{
			normalizedPaint = "none";
			return null;
		}
		if (string.Equals(paint, "transparent", StringComparison.OrdinalIgnoreCase) ||
			paint.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase))
		{
			return SvgIconReasonCodes.PaintTransparent;
		}
		if (paint.StartsWith("inherit(", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(paint, "inherit", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(paint, "initial", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(paint, "unset", StringComparison.OrdinalIgnoreCase) ||
			paint.StartsWith("var(", StringComparison.OrdinalIgnoreCase))
		{
			return SvgIconReasonCodes.PaintUnsupported;
		}

		// SVG uses RGBA hex while WPF uses ARGB. Keep the supported SVG subset explicit.
		bool rgbHex = Regex.IsMatch(paint, @"\A#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})\z", RegexOptions.CultureInvariant);
		bool namedColor = Regex.IsMatch(paint, @"\A[A-Za-z]+\z", RegexOptions.CultureInvariant);
		if (!rgbHex && !namedColor) return SvgIconReasonCodes.PaintUnsupported;
		try
		{
			object converted = ColorConverter.ConvertFromString(paint);
			if (converted is not Color color)
			{
				return SvgIconReasonCodes.PaintUnsupported;
			}
			if (color.A != byte.MaxValue)
			{
				return SvgIconReasonCodes.PaintTransparent;
			}
			normalizedPaint = color.ToString();
			return null;
		}
		catch
		{
			return SvgIconReasonCodes.PaintUnsupported;
		}
	}

	private static bool HasNonWhitespaceText(XElement element)
	{
		return element.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value));
	}

	private static bool IsAllowedNamespace(XNamespace ns)
	{
		return ns == XNamespace.None || ns == SvgNamespace;
	}

	private static SvgPathValidationResult ValidatePathData(string pathData)
	{
		if (string.IsNullOrWhiteSpace(pathData))
		{
			return SvgPathValidationResult.Failure(SvgIconReasonCodes.PathDataMissing);
		}

		string trimmed = pathData.Trim();
		foreach (Match match in NumericTokenRegex.Matches(trimmed))
		{
			if (!double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
				!double.IsFinite(value))
			{
				return SvgPathValidationResult.Failure(SvgIconReasonCodes.NonFiniteCoordinate);
			}
		}

		try
		{
			Geometry geometry = Geometry.Parse(trimmed);
			if (geometry.Bounds.IsEmpty ||
				(geometry.Bounds.Width <= 0 && geometry.Bounds.Height <= 0))
			{
				return SvgPathValidationResult.Failure(SvgIconReasonCodes.EmptyGeometry);
			}
			return SvgPathValidationResult.Success(trimmed);
		}
		catch (FormatException)
		{
			return SvgPathValidationResult.Failure(SvgIconReasonCodes.InvalidPathData);
		}
		catch (ArgumentException)
		{
			return SvgPathValidationResult.Failure(SvgIconReasonCodes.InvalidPathData);
		}
		catch (NotSupportedException)
		{
			return SvgPathValidationResult.Failure(SvgIconReasonCodes.InvalidPathData);
		}
	}

	private static bool TryWriteCanonicalPath(
		string targetDirectory,
		string? sourceFullPath,
		string displayName,
		string pathData,
		List<string> createdPaths,
		out string? targetPath,
		out string reasonCode)
	{
		string safeName = SanitizeFileName(displayName);
		string sourceKey = sourceFullPath ?? ("bare:" + displayName);
		string hash = Convert.ToHexString(
			SHA256.HashData(Encoding.UTF8.GetBytes(sourceKey + "\n" + pathData)));

		for (int attempt = 0; attempt < 5; attempt++)
		{
			string fileName = $"{safeName}_{hash[..16]}_{Guid.NewGuid():N}.svg";
			string candidate = Path.Combine(targetDirectory, fileName);
			string candidateFull;
			try
			{
				candidateFull = Path.GetFullPath(candidate);
			}
			catch
			{
				targetPath = null;
				reasonCode = SvgIconReasonCodes.IoError;
				return false;
			}

			if (sourceFullPath != null &&
				string.Equals(candidateFull, Path.GetFullPath(sourceFullPath), StringComparison.OrdinalIgnoreCase))
			{
				targetPath = null;
				reasonCode = SvgIconReasonCodes.SourceDestinationCollision;
				return false;
			}

			bool created = false;
			try
			{
				using (var stream = new FileStream(candidateFull, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
					created = true;
					using var writer = new StreamWriter(stream, Utf8WithoutBom, 4096, leaveOpen: true);
					writer.Write(pathData);
					writer.Flush();
				}
				createdPaths.Add(candidateFull);
				targetPath = candidateFull;
				reasonCode = SvgIconReasonCodes.Success;
				return true;
			}
			catch (IOException) when (File.Exists(candidateFull))
			{
				// Retry with a fresh name without touching the existing file.
				if (created)
				{
					TryDelete(candidateFull);
				}
			}
			catch
			{
				if (created)
				{
					TryDelete(candidateFull);
				}
				targetPath = null;
				reasonCode = SvgIconReasonCodes.IoError;
				return false;
			}
		}

		targetPath = null;
		reasonCode = SvgIconReasonCodes.IoError;
		return false;
	}

	private static string SanitizeFileName(string value)
	{
		string name = value.Trim();
		if (name.Length == 0)
		{
			name = "icon";
		}
		foreach (char invalid in Path.GetInvalidFileNameChars())
		{
			name = name.Replace(invalid, '_');
		}
		name = name.Trim().TrimEnd('.');
		if (name.Length == 0)
		{
			name = "icon";
		}
		return name.Length <= 80 ? name : name[..80];
	}

	private static void RollbackCreatedFiles(IEnumerable<string> createdPaths)
	{
		foreach (string path in createdPaths)
		{
			TryDelete(path);
		}
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}

	private static SvgIconBatchItemResult Failure(string sourcePath, string reasonCode)
		=> new(sourcePath, SvgIconBatchItemStatus.Failure, reasonCode);

	private static SvgIconBatchItemResult Skipped(string sourcePath, string reasonCode)
		=> new(sourcePath, SvgIconBatchItemStatus.Skipped, reasonCode);

	private sealed class DocumentValidationState
	{
		public List<string> Paths { get; } = new();
		public HashSet<string> Fills { get; } = new(StringComparer.OrdinalIgnoreCase);
	}

	private readonly struct SvgPathValidationResult
	{
		public bool IsSuccess { get; }
		public string PathData { get; }
		public string ReasonCode { get; }

		private SvgPathValidationResult(bool success, string pathData, string reasonCode)
		{
			IsSuccess = success;
			PathData = pathData;
			ReasonCode = reasonCode;
		}

		public static SvgPathValidationResult Success(string pathData)
			=> new(true, pathData, SvgIconReasonCodes.Success);

		public static SvgPathValidationResult Failure(string reasonCode)
			=> new(false, "", reasonCode);
	}
}
