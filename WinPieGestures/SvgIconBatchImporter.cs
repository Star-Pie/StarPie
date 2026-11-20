using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
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

	private const int MaxElementDepth = 64;
	private const int MaxDrawableElements = 4096;
	private const int MaxShapePoints = 16384;
	private const int MaxTotalSegments = 65536;
	private const int MaxTransformFunctions = 64;
	private const double MaxCoordinateAbs = 10_000_000.0;
	private const double MaxGeometryDimension = 1_000_000.0;
	private const double MinMatrixDeterminant = 1e-12;
	private const double MaxMatrixDeterminant = 1e12;
	private const double MaxStrokeWidth = 100_000.0;
	private const double MinMiterLimit = 1.0;
	private const double MaxMiterLimit = 10_000.0;

	private static readonly Regex NumericTokenRegex = new(
		@"[-+]?(?:(?:\d+\.\d*)|(?:\.\d+)|(?:\d+))(?:[eE][-+]?\d+)?",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex UrlFunctionRegex = new(
		@"url\s*\(",
		RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

	private static readonly Regex BenignW3cSvgDoctypeRegex = new(
		@"\G<!DOCTYPE\s+svg\s+PUBLIC\s+(?:" +
			@"(?:""-//W3C//DTD SVG 1\.1//EN""|'-//W3C//DTD SVG 1\.1//EN')\s+(?:""https?://www\.w3\.org/Graphics/SVG/1\.1/DTD/svg11\.dtd""|'https?://www\.w3\.org/Graphics/SVG/1\.1/DTD/svg11\.dtd')" +
			@"|" +
			@"(?:""-//W3C//DTD SVG 1\.0//EN""|'-//W3C//DTD SVG 1\.0//EN')\s+(?:""https?://www\.w3\.org/TR/2001/REC-SVG-20010904/DTD/svg10\.dtd""|'https?://www\.w3\.org/TR/2001/REC-SVG-20010904/DTD/svg10\.dtd')" +
			@"|" +
			@"(?:""-//W3C//DTD SVG 1\.0//EN""|'-//W3C//DTD SVG 1\.0//EN')\s+(?:""https?://www\.w3\.org/TR/SVG/DTD/svg10\.dtd""|'https?://www\.w3\.org/TR/SVG/DTD/svg10\.dtd')" +
			@"|" +
			@"(?:""-//W3C//DTD SVG 1\.1 Basic//EN""|'-//W3C//DTD SVG 1\.1 Basic//EN')\s+(?:""https?://www\.w3\.org/Graphics/SVG/1\.1/DTD/svg11-basic\.dtd""|'https?://www\.w3\.org/Graphics/SVG/1\.1/DTD/svg11-basic\.dtd')" +
			@"|" +
			@"(?:""-//W3C//DTD SVG 1\.1 Tiny//EN""|'-//W3C//DTD SVG 1\.1 Tiny//EN')\s+(?:""https?://www\.w3\.org/Graphics/SVG/1\.1/DTD/svg11-tiny\.dtd""|'https?://www\.w3\.org/Graphics/SVG/1\.1/DTD/svg11-tiny\.dtd')" +
		@")\s*>",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly string[] ForbiddenDtdKeywords =
	{
		"<!ENTITY",
		"<!ELEMENT",
		"<!ATTLIST",
		"<!NOTATION"
	};

	private static readonly HashSet<string> HarmlessMetadataAttributes = new(StringComparer.Ordinal)
	{
		"id", "t", "class", "p-id", "role", "focusable"
	};

	private static readonly HashSet<string> RootLayoutAttributes = new(StringComparer.Ordinal)
	{
		"viewBox", "width", "height", "version", "preserveAspectRatio"
	};

	private static readonly UTF8Encoding StrictUtf8WithoutBom = new(false, true);
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

			byte[] rawBytes;
			using (var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				rawBytes = ReadBoundedBytes(stream, MaxSvgFileBytes, out bool tooLarge, cancellationToken);
				if (tooLarge)
				{
					return SvgPathValidationResult.Failure(SvgIconReasonCodes.FileTooLarge);
				}
			}

			if (!TryDecodeUtf8Text(rawBytes, out string text))
			{
				return SvgPathValidationResult.Failure(SvgIconReasonCodes.InvalidXml);
			}

			if (!TryStripBenignW3cDoctype(text, out string sanitizedXml, out string? doctypeError))
			{
				return SvgPathValidationResult.Failure(doctypeError!);
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

			using var stringReader = new StringReader(sanitizedXml);
			using var reader = XmlReader.Create(stringReader, settings);
			cancellationToken.ThrowIfCancellationRequested();
			XDocument document = XDocument.Load(reader, LoadOptions.None);
			cancellationToken.ThrowIfCancellationRequested();
			return ValidateDocument(document, cancellationToken);
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

	private static byte[] ReadBoundedBytes(Stream stream, long maxBytes, out bool tooLarge, CancellationToken cancellationToken)
	{
		using var memory = new MemoryStream();
		byte[] buffer = new byte[16 * 1024];
		long total = 0;
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			int read = stream.Read(buffer, 0, buffer.Length);
			if (read == 0)
			{
				tooLarge = false;
				return memory.ToArray();
			}
			total += read;
			if (total > maxBytes)
			{
				tooLarge = true;
				return Array.Empty<byte>();
			}
			memory.Write(buffer, 0, read);
		}
	}

	private static bool TryDecodeUtf8Text(byte[] rawBytes, out string text)
	{
		int offset = 0;
		if (rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF)
		{
			offset = 3;
		}

		try
		{
			text = StrictUtf8WithoutBom.GetString(rawBytes, offset, rawBytes.Length - offset);
			if (text.Length > 0 && text[0] == '\uFEFF')
			{
				text = text[1..];
			}
			return !string.IsNullOrWhiteSpace(text);
		}
		catch (DecoderFallbackException)
		{
			text = "";
			return false;
		}
	}

	private static bool TryStripBenignW3cDoctype(string text, out string sanitizedXml, out string? errorCode)
	{
		sanitizedXml = text;
		errorCode = null;

		foreach (string keyword in ForbiddenDtdKeywords)
		{
			if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
			{
				errorCode = SvgIconReasonCodes.DtdForbidden;
				return false;
			}
		}

		// Walk only the XML prolog before the root element and strip at most one exact
		// benign W3C SVG 1.0/1.1 DOCTYPE without any '[' internal subset. Any non-whitelisted
		// or second DOCTYPE fails immediately with DtdForbidden.
		int pos = 0;
		bool strippedBenign = false;
		while (pos < sanitizedXml.Length)
		{
			while (pos < sanitizedXml.Length && char.IsWhiteSpace(sanitizedXml[pos]))
			{
				pos++;
			}
			if (pos >= sanitizedXml.Length || sanitizedXml[pos] != '<')
			{
				break;
			}
			if (sanitizedXml.AsSpan(pos).StartsWith("<?", StringComparison.Ordinal))
			{
				int close = sanitizedXml.IndexOf("?>", pos + 2, StringComparison.Ordinal);
				if (close < 0)
				{
					break;
				}
				pos = close + 2;
				continue;
			}
			if (sanitizedXml.AsSpan(pos).StartsWith("<!--", StringComparison.Ordinal))
			{
				int close = sanitizedXml.IndexOf("-->", pos + 4, StringComparison.Ordinal);
				if (close < 0)
				{
					break;
				}
				pos = close + 3;
				continue;
			}
			if (sanitizedXml.AsSpan(pos).StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
			{
				if (strippedBenign)
				{
					errorCode = SvgIconReasonCodes.DtdForbidden;
					return false;
				}
				Match match = BenignW3cSvgDoctypeRegex.Match(sanitizedXml, pos);
				if (!match.Success || match.Value.Contains('[', StringComparison.Ordinal))
				{
					errorCode = SvgIconReasonCodes.DtdForbidden;
					return false;
				}
				sanitizedXml = sanitizedXml.Remove(match.Index, match.Length);
				strippedBenign = true;
				continue;
			}
			break;
		}

		return true;
	}

	private static SvgPathValidationResult ValidateDocument(XDocument document, CancellationToken cancellationToken)
	{
		XElement? root = document.Root;
		if (root == null || root.Name.LocalName != "svg" || !IsAllowedNamespace(root.Name.Namespace))
		{
			return SvgPathValidationResult.Failure(SvgIconReasonCodes.InvalidRoot);
		}

		var docState = new DocumentValidationState();
		var initialRenderState = ElementRenderState.CreateRootDefault();
		string? reason = ValidateElement(root, initialRenderState, true, 0, docState, cancellationToken);
		if (reason != null)
		{
			return SvgPathValidationResult.Failure(reason);
		}
		if (docState.Elements.Count == 0)
		{
			return SvgPathValidationResult.Failure(
				docState.SawInvisibleGeometry
					? SvgIconReasonCodes.InvisiblePath
					: SvgIconReasonCodes.PathDataMissing);
		}
		if (docState.PaintedColors.Count > 1)
		{
			return SvgPathValidationResult.Failure(SvgIconReasonCodes.MultiColor);
		}

		return BuildCanonicalPathData(docState);
	}

	private static string? ValidateElement(
		XElement element,
		ElementRenderState parentState,
		bool isRoot,
		int depth,
		DocumentValidationState docState,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (depth > MaxElementDepth)
		{
			return SvgIconReasonCodes.UnsupportedElement;
		}
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
				return ValidateContainer(element, ElementKind.Svg, parentState, depth, docState, cancellationToken);
			case "g":
				return ValidateContainer(element, ElementKind.Group, parentState, depth, docState, cancellationToken);
			case "path":
				return ValidateDrawableElement(element, ElementKind.Path, parentState, docState);
			case "circle":
				return ValidateDrawableElement(element, ElementKind.Circle, parentState, docState);
			case "ellipse":
				return ValidateDrawableElement(element, ElementKind.Ellipse, parentState, docState);
			case "rect":
				return ValidateDrawableElement(element, ElementKind.Rect, parentState, docState);
			case "line":
				return ValidateDrawableElement(element, ElementKind.Line, parentState, docState);
			case "polygon":
				return ValidateDrawableElement(element, ElementKind.Polygon, parentState, docState);
			case "polyline":
				return ValidateDrawableElement(element, ElementKind.Polyline, parentState, docState);
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
			case "style":
				return SvgIconReasonCodes.StyleUnsupported;
			default:
				return SvgIconReasonCodes.UnsupportedElement;
		}
	}

	private static string? ValidateContainer(
		XElement element,
		ElementKind kind,
		ElementRenderState parentState,
		int depth,
		DocumentValidationState docState,
		CancellationToken cancellationToken)
	{
		string? reason = ValidateAttributes(element, kind, parentState, out ElementRenderState effectiveState);
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
			reason = ValidateElement(child, effectiveState, false, depth + 1, docState, cancellationToken);
			if (reason != null)
			{
				return reason;
			}
		}
		return null;
	}

	private static string? ValidateDrawableElement(
		XElement element,
		ElementKind kind,
		ElementRenderState parentState,
		DocumentValidationState docState)
	{
		if (docState.DrawableCount >= MaxDrawableElements)
		{
			return SvgIconReasonCodes.InvalidPathData;
		}
		docState.DrawableCount++;

		string? reason = ValidateAttributes(element, kind, parentState, out ElementRenderState state);
		if (reason != null)
		{
			return reason;
		}
		if (HasNonWhitespaceText(element))
		{
			return SvgIconReasonCodes.UnsupportedText;
		}

		if (!TryBuildLocalShapeGeometry(
			element,
			kind,
			state.FillRule,
			out PathGeometry localGeometry,
			out bool canFill,
			out string? rawPathData,
			out string? shapeError))
		{
			return shapeError;
		}

		bool hasFill = canFill && !string.Equals(state.Fill, "none", StringComparison.OrdinalIgnoreCase);
		bool hasStroke = !string.Equals(state.Stroke, "none", StringComparison.OrdinalIgnoreCase) && state.StrokeWidth > 0.0;
		if (!hasFill && !hasStroke)
		{
			docState.SawInvisibleGeometry = true;
			return null;
		}

		if (hasFill)
		{
			docState.PaintedColors.Add(state.Fill);
		}
		if (hasStroke)
		{
			docState.PaintedColors.Add(state.Stroke);
		}

		bool isRawEligible = kind == ElementKind.Path &&
			hasFill &&
			!hasStroke &&
			state.Transform.IsIdentity &&
			rawPathData != null;

		PathGeometry? fillGeometry = null;
		if (hasFill)
		{
			if (!TransformPathGeometry(localGeometry, state.Transform, out fillGeometry, out string? txError))
			{
				return txError;
			}
			if (IsBoundsEmptyOrZero(fillGeometry.Bounds))
			{
				if (!hasStroke)
				{
					return SvgIconReasonCodes.EmptyGeometry;
				}
				fillGeometry = null;
			}
		}

		PathGeometry? widenedStrokeGeometry = null;
		if (hasStroke)
		{
			var pen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.Black, state.StrokeWidth)
			{
				StartLineCap = state.StrokeLineCap,
				EndLineCap = state.StrokeLineCap,
				LineJoin = state.StrokeLineJoin,
				MiterLimit = state.StrokeMiterLimit
			};
			pen.Freeze();

			PathGeometry localWidened;
			try
			{
				double span = Math.Max(Math.Max(localGeometry.Bounds.Width, localGeometry.Bounds.Height), state.StrokeWidth);
				double tolerance = Math.Clamp(span * 0.005, 1e-4, 0.25);
				localWidened = localGeometry.GetWidenedPathGeometry(pen, tolerance, ToleranceType.Absolute);
			}
			catch
			{
				return SvgIconReasonCodes.InvalidPathData;
			}

			if (!TransformPathGeometry(localWidened, state.Transform, out widenedStrokeGeometry, out string? strokeTxError))
			{
				return strokeTxError;
			}

			widenedStrokeGeometry = Geometry.Combine(widenedStrokeGeometry, Geometry.Empty, GeometryCombineMode.Union, null);
			if (IsBoundsEmptyOrZero(widenedStrokeGeometry.Bounds))
			{
				widenedStrokeGeometry = null;
				if (fillGeometry == null)
				{
					return SvgIconReasonCodes.EmptyGeometry;
				}
			}
		}

		if (fillGeometry != null)
		{
			if (docState.TotalSegmentCount + CountSegments(fillGeometry) > MaxTotalSegments)
			{
				return SvgIconReasonCodes.InvalidPathData;
			}
			docState.TotalSegmentCount += CountSegments(fillGeometry);
			docState.Elements.Add(new CollectedGeometry(fillGeometry, isRawEligible ? rawPathData : null, isRawEligible));
		}

		if (widenedStrokeGeometry != null)
		{
			if (docState.TotalSegmentCount + CountSegments(widenedStrokeGeometry) > MaxTotalSegments)
			{
				return SvgIconReasonCodes.InvalidPathData;
			}
			docState.TotalSegmentCount += CountSegments(widenedStrokeGeometry);
			docState.Elements.Add(new CollectedGeometry(widenedStrokeGeometry, null, false));
		}

		return null;
	}

	private static string? ValidateHarmlessMetadataElement(XElement element)
	{
		return ValidateAttributes(
			element,
			ElementKind.MetadataText,
			ElementRenderState.CreateRootDefault(),
			out _);
	}

	private static string? ValidateAttributes(
		XElement element,
		ElementKind kind,
		ElementRenderState parentState,
		out ElementRenderState state)
	{
		state = parentState;
		Matrix localTransform = Matrix.Identity;
		bool isDrawableOrContainer = kind != ElementKind.MetadataText;

		foreach (XAttribute attribute in element.Attributes())
		{
			if (attribute.IsNamespaceDeclaration)
			{
				continue;
			}

			string name = attribute.Name.LocalName;
			string rawValue = attribute.Value;

			if (attribute.Name.Namespace == XNamespace.Xml && name == "space")
			{
				if (UrlFunctionRegex.IsMatch(rawValue))
				{
					return SvgIconReasonCodes.ExternalReference;
				}
				string trimmedSpace = rawValue.Trim();
				if (trimmedSpace != "default" && trimmedSpace != "preserve")
				{
					return SvgIconReasonCodes.UnsupportedAttribute;
				}
				continue;
			}

			if (name == "href")
			{
				return SvgIconReasonCodes.ExternalReference;
			}
			if (attribute.Name.Namespace != XNamespace.None)
			{
				return SvgIconReasonCodes.UnsupportedAttribute;
			}
			if (name == "style")
			{
				return SvgIconReasonCodes.StyleUnsupported;
			}
			if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
			{
				return SvgIconReasonCodes.UnsupportedAttribute;
			}
			if (UrlFunctionRegex.IsMatch(rawValue))
			{
				return name is "fill" or "stroke"
					? SvgIconReasonCodes.GradientUnsupported
					: SvgIconReasonCodes.ExternalReference;
			}

			if (IsHarmlessMetadataAttribute(name))
			{
				continue;
			}

			if (kind == ElementKind.Svg && RootLayoutAttributes.Contains(name))
			{
				continue;
			}

			if (!isDrawableOrContainer)
			{
				return SvgIconReasonCodes.UnsupportedAttribute;
			}

			switch (name)
			{
				case "fill":
				{
					string trimmed = rawValue.Trim();
					if (trimmed.Length == 0)
					{
						return SvgIconReasonCodes.UnsupportedAttribute;
					}
					string? paintReason = NormalizePaint(trimmed, out string normalizedPaint);
					if (paintReason != null)
					{
						return paintReason;
					}
					state = state with { Fill = normalizedPaint };
					break;
				}
				case "fill-rule":
				{
					string trimmed = rawValue.Trim();
					if (string.Equals(trimmed, "evenodd", StringComparison.OrdinalIgnoreCase))
					{
						state = state with { FillRule = FillRule.EvenOdd };
					}
					else if (string.Equals(trimmed, "nonzero", StringComparison.OrdinalIgnoreCase))
					{
						state = state with { FillRule = FillRule.Nonzero };
					}
					else if (!string.Equals(trimmed, "inherit", StringComparison.OrdinalIgnoreCase))
					{
						return SvgIconReasonCodes.UnsupportedAttribute;
					}
					break;
				}
				case "clip-rule":
				{
					string trimmed = rawValue.Trim();
					if (!string.Equals(trimmed, "evenodd", StringComparison.OrdinalIgnoreCase) &&
						!string.Equals(trimmed, "nonzero", StringComparison.OrdinalIgnoreCase) &&
						!string.Equals(trimmed, "inherit", StringComparison.OrdinalIgnoreCase))
					{
						return SvgIconReasonCodes.UnsupportedAttribute;
					}
					break;
				}
				case "stroke":
				{
					string trimmed = rawValue.Trim();
					if (trimmed.Length == 0)
					{
						return SvgIconReasonCodes.StrokeUnsupported;
					}
					string? paintReason = NormalizePaint(trimmed, out string normalizedPaint);
					if (paintReason != null)
					{
						return paintReason;
					}
					state = state with { Stroke = normalizedPaint };
					break;
				}
				case "stroke-width":
				{
					if (!TryParseSvgNumber(rawValue, allowPxUnit: true, out double width, out bool isNonFinite))
					{
						return isNonFinite ? SvgIconReasonCodes.NonFiniteCoordinate : SvgIconReasonCodes.StrokeUnsupported;
					}
					if (width < 0.0 || width > MaxStrokeWidth)
					{
						return SvgIconReasonCodes.StrokeUnsupported;
					}
					state = state with { StrokeWidth = width };
					break;
				}
				case "stroke-linecap":
				{
					string trimmed = rawValue.Trim();
					if (string.Equals(trimmed, "butt", StringComparison.OrdinalIgnoreCase))
					{
						state = state with { StrokeLineCap = PenLineCap.Flat };
					}
					else if (string.Equals(trimmed, "round", StringComparison.OrdinalIgnoreCase))
					{
						state = state with { StrokeLineCap = PenLineCap.Round };
					}
					else if (string.Equals(trimmed, "square", StringComparison.OrdinalIgnoreCase))
					{
						state = state with { StrokeLineCap = PenLineCap.Square };
					}
					else if (!string.Equals(trimmed, "inherit", StringComparison.OrdinalIgnoreCase))
					{
						return SvgIconReasonCodes.StrokeUnsupported;
					}
					break;
				}
				case "stroke-linejoin":
				{
					string trimmed = rawValue.Trim();
					if (string.Equals(trimmed, "miter", StringComparison.OrdinalIgnoreCase))
					{
						state = state with { StrokeLineJoin = PenLineJoin.Miter };
					}
					else if (string.Equals(trimmed, "round", StringComparison.OrdinalIgnoreCase))
					{
						state = state with { StrokeLineJoin = PenLineJoin.Round };
					}
					else if (string.Equals(trimmed, "bevel", StringComparison.OrdinalIgnoreCase))
					{
						state = state with { StrokeLineJoin = PenLineJoin.Bevel };
					}
					else if (!string.Equals(trimmed, "inherit", StringComparison.OrdinalIgnoreCase))
					{
						return SvgIconReasonCodes.StrokeUnsupported;
					}
					break;
				}
				case "stroke-miterlimit":
				{
					if (!TryParseSvgNumber(rawValue, allowPxUnit: false, out double miterLimit, out bool isNonFinite))
					{
						return isNonFinite ? SvgIconReasonCodes.NonFiniteCoordinate : SvgIconReasonCodes.StrokeUnsupported;
					}
					if (miterLimit < MinMiterLimit || miterLimit > MaxMiterLimit)
					{
						return SvgIconReasonCodes.StrokeUnsupported;
					}
					state = state with { StrokeMiterLimit = miterLimit };
					break;
				}
				case "transform":
				{
					if (!TryParseTransform(rawValue, out Matrix parsedTransform, out string? transformError))
					{
						return transformError;
					}
					localTransform = Matrix.Multiply(parsedTransform, localTransform);
					break;
				}
				default:
				{
					if (name.StartsWith("stroke-", StringComparison.Ordinal))
					{
						return SvgIconReasonCodes.StrokeUnsupported;
					}
					if (!IsShapeGeometryAttribute(kind, name))
					{
						return SvgIconReasonCodes.UnsupportedAttribute;
					}
					break;
				}
			}
		}

		if (isDrawableOrContainer)
		{
			Matrix composed = Matrix.Multiply(localTransform, parentState.Transform);
			if (!IsTransformMatrixValid(composed))
			{
				return SvgIconReasonCodes.TransformUnsupported;
			}
			state = state with { Transform = composed };
		}

		return null;
	}

	private static bool IsHarmlessMetadataAttribute(string name)
	{
		return HarmlessMetadataAttributes.Contains(name) ||
			(name.StartsWith("data-", StringComparison.Ordinal) && name.Length > 5) ||
			(name.StartsWith("aria-", StringComparison.Ordinal) && name.Length > 5);
	}

	private static bool IsShapeGeometryAttribute(ElementKind kind, string name)
	{
		return kind switch
		{
			ElementKind.Path => name == "d",
			ElementKind.Circle => name is "cx" or "cy" or "r",
			ElementKind.Ellipse => name is "cx" or "cy" or "rx" or "ry",
			ElementKind.Rect => name is "x" or "y" or "width" or "height" or "rx" or "ry",
			ElementKind.Line => name is "x1" or "y1" or "x2" or "y2",
			ElementKind.Polygon or ElementKind.Polyline => name == "points",
			_ => false
		};
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
		if (paint.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && paint.EndsWith(")", StringComparison.Ordinal))
		{
			return TryParseRgbColor(paint, out normalizedPaint);
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
			normalizedPaint = color.ToString(CultureInfo.InvariantCulture);
			return null;
		}
		catch
		{
			return SvgIconReasonCodes.PaintUnsupported;
		}
	}

	private static string? TryParseRgbColor(string paint, out string normalizedPaint)
	{
		normalizedPaint = "";
		string inner = paint[4..^1].Trim();
		if (inner.Length == 0)
		{
			return SvgIconReasonCodes.PaintUnsupported;
		}

		int slashIndex = inner.IndexOf('/');
		if (slashIndex >= 0)
		{
			string alphaPart = inner[(slashIndex + 1)..].Trim();
			inner = inner[..slashIndex].Trim();
			if (!TryParseAlpha(alphaPart, out double alpha))
			{
				return SvgIconReasonCodes.PaintUnsupported;
			}
			if (alpha < 1.0)
			{
				return SvgIconReasonCodes.PaintTransparent;
			}
		}

		string[] parts = inner.Contains(',')
			? inner.Split(',')
			: inner.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 4)
		{
			if (!TryParseAlpha(parts[3].Trim(), out double alpha))
			{
				return SvgIconReasonCodes.PaintUnsupported;
			}
			if (alpha < 1.0)
			{
				return SvgIconReasonCodes.PaintTransparent;
			}
			parts = new[] { parts[0], parts[1], parts[2] };
		}
		if (parts.Length != 3)
		{
			return SvgIconReasonCodes.PaintUnsupported;
		}

		bool? expectPercent = null;
		Span<byte> rgb = stackalloc byte[3];
		for (int i = 0; i < 3; i++)
		{
			string token = parts[i].Trim();
			if (token.Length == 0)
			{
				return SvgIconReasonCodes.PaintUnsupported;
			}
			bool isPercent = token.EndsWith("%", StringComparison.Ordinal);
			if (expectPercent.HasValue && expectPercent.Value != isPercent)
			{
				return SvgIconReasonCodes.PaintUnsupported;
			}
			expectPercent = isPercent;

			string numStr = isPercent ? token[..^1].Trim() : token;
			if (!TryParseSvgNumber(numStr, allowPxUnit: false, out double channel, out _))
			{
				return SvgIconReasonCodes.PaintUnsupported;
			}
			double scaled = isPercent ? channel * 2.55 : channel;
			rgb[i] = (byte)Math.Clamp(Math.Round(scaled, MidpointRounding.AwayFromZero), 0.0, 255.0);
		}

		normalizedPaint = Color.FromRgb(rgb[0], rgb[1], rgb[2]).ToString(CultureInfo.InvariantCulture);
		return null;
	}

	private static bool TryParseAlpha(string token, out double alpha)
	{
		alpha = 0.0;
		if (token.Length == 0)
		{
			return false;
		}
		bool isPercent = token.EndsWith("%", StringComparison.Ordinal);
		string numStr = isPercent ? token[..^1].Trim() : token;
		if (!TryParseSvgNumber(numStr, allowPxUnit: false, out double raw, out _))
		{
			return false;
		}
		alpha = Math.Clamp(isPercent ? raw / 100.0 : raw, 0.0, 1.0);
		return true;
	}

	private static bool TryBuildLocalShapeGeometry(
		XElement element,
		ElementKind kind,
		FillRule fillRule,
		out PathGeometry geometry,
		out bool canFill,
		out string? rawPathData,
		out string? errorCode)
	{
		geometry = new PathGeometry { FillRule = fillRule };
		canFill = false;
		rawPathData = null;
		errorCode = null;

		switch (kind)
		{
			case ElementKind.Path:
			{
				string? pathData = element.Attribute("d")?.Value;
				if (string.IsNullOrWhiteSpace(pathData))
				{
					errorCode = SvgIconReasonCodes.PathDataMissing;
					return false;
				}
				string trimmed = pathData.Trim();
				if (trimmed[0] != 'M' && trimmed[0] != 'm')
				{
					errorCode = SvgIconReasonCodes.InvalidPathData;
					return false;
				}
				SvgPathValidationResult validation = ValidatePathData(trimmed);
				if (!validation.IsSuccess)
				{
					errorCode = validation.ReasonCode;
					return false;
				}
				geometry = PathGeometry.CreateFromGeometry(Geometry.Parse(validation.PathData));
				geometry.FillRule = fillRule;
				if (CountSegments(geometry) > MaxTotalSegments || IsBoundsRunaway(geometry.Bounds))
				{
					errorCode = SvgIconReasonCodes.InvalidPathData;
					return false;
				}
				canFill = true;
				rawPathData = validation.PathData;
				return true;
			}
			case ElementKind.Circle:
			{
				if (!TryReadOptionalCoord(element, "cx", 0.0, out double cx, out errorCode) ||
					!TryReadOptionalCoord(element, "cy", 0.0, out double cy, out errorCode) ||
					!TryReadOptionalCoord(element, "r", 0.0, out double r, out errorCode))
				{
					return false;
				}
				if (r < 0.0)
				{
					errorCode = SvgIconReasonCodes.InvalidPathData;
					return false;
				}
				if (r == 0.0)
				{
					errorCode = SvgIconReasonCodes.EmptyGeometry;
					return false;
				}
				geometry = PathGeometry.CreateFromGeometry(new EllipseGeometry(new Point(cx, cy), r, r));
				geometry.FillRule = fillRule;
				if (IsBoundsRunaway(geometry.Bounds))
				{
					errorCode = SvgIconReasonCodes.InvalidPathData;
					return false;
				}
				canFill = true;
				return true;
			}
			case ElementKind.Ellipse:
			{
				if (!TryReadOptionalCoord(element, "cx", 0.0, out double cx, out errorCode) ||
					!TryReadOptionalCoord(element, "cy", 0.0, out double cy, out errorCode) ||
					!TryReadOptionalCoord(element, "rx", 0.0, out double rx, out errorCode) ||
					!TryReadOptionalCoord(element, "ry", 0.0, out double ry, out errorCode))
				{
					return false;
				}
				if (rx < 0.0 || ry < 0.0)
				{
					errorCode = SvgIconReasonCodes.InvalidPathData;
					return false;
				}
				if (rx == 0.0 || ry == 0.0)
				{
					errorCode = SvgIconReasonCodes.EmptyGeometry;
					return false;
				}
				geometry = PathGeometry.CreateFromGeometry(new EllipseGeometry(new Point(cx, cy), rx, ry));
				geometry.FillRule = fillRule;
				if (IsBoundsRunaway(geometry.Bounds))
				{
					errorCode = SvgIconReasonCodes.InvalidPathData;
					return false;
				}
				canFill = true;
				return true;
			}
			case ElementKind.Rect:
			{
				if (!TryReadOptionalCoord(element, "x", 0.0, out double x, out errorCode) ||
					!TryReadOptionalCoord(element, "y", 0.0, out double y, out errorCode) ||
					!TryReadOptionalCoord(element, "width", 0.0, out double width, out errorCode) ||
					!TryReadOptionalCoord(element, "height", 0.0, out double height, out errorCode))
				{
					return false;
				}
				if (width < 0.0 || height < 0.0)
				{
					errorCode = SvgIconReasonCodes.InvalidPathData;
					return false;
				}
				if (width == 0.0 || height == 0.0)
				{
					errorCode = SvgIconReasonCodes.EmptyGeometry;
					return false;
				}

				XAttribute? rxAttr = element.Attribute("rx");
				XAttribute? ryAttr = element.Attribute("ry");
				double rx = 0.0;
				double ry = 0.0;
				if (rxAttr != null)
				{
					if (!TryReadOptionalCoord(element, "rx", 0.0, out rx, out errorCode))
					{
						return false;
					}
					if (rx < 0.0)
					{
						errorCode = SvgIconReasonCodes.InvalidPathData;
						return false;
					}
				}
				if (ryAttr != null)
				{
					if (!TryReadOptionalCoord(element, "ry", 0.0, out ry, out errorCode))
					{
						return false;
					}
					if (ry < 0.0)
					{
						errorCode = SvgIconReasonCodes.InvalidPathData;
						return false;
					}
				}
				if (rxAttr != null && ryAttr == null)
				{
					ry = rx;
				}
				else if (ryAttr != null && rxAttr == null)
				{
					rx = ry;
				}
				rx = Math.Min(rx, width * 0.5);
				ry = Math.Min(ry, height * 0.5);

				geometry = PathGeometry.CreateFromGeometry(new RectangleGeometry(new Rect(x, y, width, height), rx, ry));
				geometry.FillRule = fillRule;
				if (IsBoundsRunaway(geometry.Bounds))
				{
					errorCode = SvgIconReasonCodes.InvalidPathData;
					return false;
				}
				canFill = true;
				return true;
			}
			case ElementKind.Line:
			{
				if (!TryReadOptionalCoord(element, "x1", 0.0, out double x1, out errorCode) ||
					!TryReadOptionalCoord(element, "y1", 0.0, out double y1, out errorCode) ||
					!TryReadOptionalCoord(element, "x2", 0.0, out double x2, out errorCode) ||
					!TryReadOptionalCoord(element, "y2", 0.0, out double y2, out errorCode))
				{
					return false;
				}
				var figure = new PathFigure
				{
					StartPoint = new Point(x1, y1),
					IsFilled = false,
					IsClosed = false
				};
				figure.Segments.Add(new LineSegment(new Point(x2, y2), isStroked: true));
				geometry.Figures.Add(figure);
				if (IsBoundsRunaway(geometry.Bounds))
				{
					errorCode = SvgIconReasonCodes.InvalidPathData;
					return false;
				}
				canFill = false;
				return true;
			}
			case ElementKind.Polygon:
			case ElementKind.Polyline:
			{
				string? pointsRaw = element.Attribute("points")?.Value;
				if (string.IsNullOrWhiteSpace(pointsRaw))
				{
					errorCode = SvgIconReasonCodes.PathDataMissing;
					return false;
				}
				if (!TryParseNumberList(pointsRaw, MaxShapePoints * 2, out List<double> coords, out errorCode))
				{
					return false;
				}
				if (coords.Count % 2 != 0)
				{
					errorCode = SvgIconReasonCodes.InvalidPathData;
					return false;
				}
				int pointCount = coords.Count / 2;
				if (pointCount < 2)
				{
					errorCode = SvgIconReasonCodes.EmptyGeometry;
					return false;
				}

				var points = new List<Point>(pointCount - 1);
				for (int i = 2; i < coords.Count; i += 2)
				{
					points.Add(new Point(coords[i], coords[i + 1]));
				}
				var figure = new PathFigure
				{
					StartPoint = new Point(coords[0], coords[1]),
					IsFilled = true,
					IsClosed = kind == ElementKind.Polygon
				};
				figure.Segments.Add(new PolyLineSegment(points, isStroked: true));
				geometry.Figures.Add(figure);
				if (IsBoundsRunaway(geometry.Bounds))
				{
					errorCode = SvgIconReasonCodes.InvalidPathData;
					return false;
				}
				canFill = pointCount >= 3;
				return true;
			}
			default:
				errorCode = SvgIconReasonCodes.UnsupportedElement;
				return false;
		}
	}

	private static bool TryReadOptionalCoord(XElement element, string attrName, double defaultValue, out double value, out string? errorCode)
	{
		errorCode = null;
		XAttribute? attr = element.Attribute(attrName);
		if (attr == null)
		{
			value = defaultValue;
			return true;
		}
		if (!TryParseSvgNumber(attr.Value, allowPxUnit: true, out value, out bool isNonFinite))
		{
			errorCode = isNonFinite ? SvgIconReasonCodes.NonFiniteCoordinate : SvgIconReasonCodes.InvalidPathData;
			return false;
		}
		if (Math.Abs(value) > MaxCoordinateAbs)
		{
			errorCode = SvgIconReasonCodes.NonFiniteCoordinate;
			return false;
		}
		return true;
	}

	private static bool TryParseSvgNumber(string raw, bool allowPxUnit, out double value, out bool isNonFinite)
	{
		value = 0.0;
		isNonFinite = false;
		string trimmed = raw.Trim();
		if (trimmed.Length == 0)
		{
			return false;
		}
		if (allowPxUnit && trimmed.EndsWith("px", StringComparison.OrdinalIgnoreCase))
		{
			trimmed = trimmed[..^2].TrimEnd();
			if (trimmed.Length == 0)
			{
				return false;
			}
		}

		if (string.Equals(trimmed, "NaN", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(trimmed, "Infinity", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(trimmed, "+Infinity", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(trimmed, "-Infinity", StringComparison.OrdinalIgnoreCase))
		{
			isNonFinite = true;
			return false;
		}

		Match m = NumericTokenRegex.Match(trimmed);
		if (!m.Success || m.Index != 0 || m.Length != trimmed.Length)
		{
			return false;
		}

		if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
			!double.IsFinite(value))
		{
			isNonFinite = true;
			return false;
		}
		return true;
	}

	private static bool TryParseNumberList(
		string raw,
		int maxNumbers,
		out List<double> numbers,
		out string? errorCode,
		bool enforceCoordinateLimit = true)
	{
		numbers = new List<double>();
		errorCode = null;
		int pos = 0;
		int length = raw.Length;

		while (pos < length && char.IsWhiteSpace(raw[pos]))
		{
			pos++;
		}

		while (pos < length)
		{
			if (raw[pos] == ',')
			{
				errorCode = SvgIconReasonCodes.InvalidPathData;
				return false;
			}

			Match match = NumericTokenRegex.Match(raw, pos);
			if (!match.Success || match.Index != pos)
			{
				string tail = raw[pos..];
				if (tail.StartsWith("NaN", StringComparison.OrdinalIgnoreCase) ||
					tail.StartsWith("Infinity", StringComparison.OrdinalIgnoreCase) ||
					tail.StartsWith("+Infinity", StringComparison.OrdinalIgnoreCase) ||
					tail.StartsWith("-Infinity", StringComparison.OrdinalIgnoreCase))
				{
					errorCode = SvgIconReasonCodes.NonFiniteCoordinate;
				}
				else
				{
					errorCode = SvgIconReasonCodes.InvalidPathData;
				}
				return false;
			}

			if (!double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
				!double.IsFinite(value))
			{
				errorCode = SvgIconReasonCodes.NonFiniteCoordinate;
				return false;
			}
			if (enforceCoordinateLimit && Math.Abs(value) > MaxCoordinateAbs)
			{
				errorCode = SvgIconReasonCodes.InvalidPathData;
				return false;
			}

			numbers.Add(value);
			if (numbers.Count > maxNumbers)
			{
				errorCode = SvgIconReasonCodes.InvalidPathData;
				return false;
			}

			pos += match.Length;
			bool hadWhitespace = false;
			while (pos < length && char.IsWhiteSpace(raw[pos]))
			{
				hadWhitespace = true;
				pos++;
			}
			if (pos < length && raw[pos] == ',')
			{
				pos++;
				while (pos < length && char.IsWhiteSpace(raw[pos]))
				{
					pos++;
				}
				if (pos >= length || raw[pos] == ',')
				{
					errorCode = SvgIconReasonCodes.InvalidPathData;
					return false;
				}
				continue;
			}
			_ = hadWhitespace;
		}

		return true;
	}

	private static bool TryParseTransform(string raw, out Matrix result, out string? errorCode)
	{
		result = Matrix.Identity;
		errorCode = null;
		string text = raw.Trim();
		if (text.Length == 0)
		{
			errorCode = SvgIconReasonCodes.TransformUnsupported;
			return false;
		}

		int pos = 0;
		int count = 0;
		while (pos < text.Length)
		{
			while (pos < text.Length && char.IsWhiteSpace(text[pos]))
			{
				pos++;
			}
			if (pos >= text.Length)
			{
				break;
			}

			int nameStart = pos;
			while (pos < text.Length && char.IsAsciiLetter(text[pos]))
			{
				pos++;
			}
			if (pos == nameStart)
			{
				errorCode = SvgIconReasonCodes.TransformUnsupported;
				return false;
			}
			string fnName = text[nameStart..pos];

			while (pos < text.Length && char.IsWhiteSpace(text[pos]))
			{
				pos++;
			}
			if (pos >= text.Length || text[pos] != '(')
			{
				errorCode = SvgIconReasonCodes.TransformUnsupported;
				return false;
			}
			pos++;

			int closeParen = text.IndexOf(')', pos);
			if (closeParen < 0)
			{
				errorCode = SvgIconReasonCodes.TransformUnsupported;
				return false;
			}
			string argsBody = text[pos..closeParen];
			if (argsBody.Contains('(', StringComparison.Ordinal))
			{
				errorCode = SvgIconReasonCodes.TransformUnsupported;
				return false;
			}
			pos = closeParen + 1;

			if (!TryParseNumberList(argsBody, 6, out List<double> args, out string? numError, enforceCoordinateLimit: false))
			{
				errorCode = numError == SvgIconReasonCodes.NonFiniteCoordinate
					? SvgIconReasonCodes.NonFiniteCoordinate
					: SvgIconReasonCodes.TransformUnsupported;
				return false;
			}

			if (!TryBuildTransformOperation(fnName, args, out Matrix op))
			{
				errorCode = SvgIconReasonCodes.TransformUnsupported;
				return false;
			}

			result = Matrix.Multiply(op, result);
			if (!IsTransformMatrixValid(result))
			{
				errorCode = SvgIconReasonCodes.TransformUnsupported;
				return false;
			}

			count++;
			if (count > MaxTransformFunctions)
			{
				errorCode = SvgIconReasonCodes.TransformUnsupported;
				return false;
			}

			while (pos < text.Length && char.IsWhiteSpace(text[pos]))
			{
				pos++;
			}
			if (pos < text.Length && text[pos] == ',')
			{
				pos++;
				while (pos < text.Length && char.IsWhiteSpace(text[pos]))
				{
					pos++;
				}
				if (pos >= text.Length || text[pos] == ',')
				{
					errorCode = SvgIconReasonCodes.TransformUnsupported;
					return false;
				}
			}
		}

		return true;
	}

	private static bool TryBuildTransformOperation(string fnName, List<double> args, out Matrix matrix)
	{
		matrix = Matrix.Identity;
		switch (fnName)
		{
			case "matrix":
				if (args.Count != 6)
				{
					return false;
				}
				matrix = new Matrix(args[0], args[1], args[2], args[3], args[4], args[5]);
				return IsTransformMatrixValid(matrix);
			case "translate":
				if (args.Count is not (1 or 2))
				{
					return false;
				}
				matrix.Translate(args[0], args.Count == 2 ? args[1] : 0.0);
				return IsTransformMatrixValid(matrix);
			case "scale":
				if (args.Count is not (1 or 2))
				{
					return false;
				}
				matrix.Scale(args[0], args.Count == 2 ? args[1] : args[0]);
				return IsTransformMatrixValid(matrix);
			case "rotate":
				if (args.Count == 1)
				{
					matrix.Rotate(args[0]);
					return IsTransformMatrixValid(matrix);
				}
				if (args.Count == 3)
				{
					matrix.RotateAt(args[0], args[1], args[2]);
					return IsTransformMatrixValid(matrix);
				}
				return false;
			case "skewX":
			{
				if (args.Count != 1)
				{
					return false;
				}
				double rad = args[0] * (Math.PI / 180.0);
				if (Math.Abs(Math.Cos(rad)) < 1e-9)
				{
					return false;
				}
				matrix = new Matrix(1.0, 0.0, Math.Tan(rad), 1.0, 0.0, 0.0);
				return IsTransformMatrixValid(matrix);
			}
			case "skewY":
			{
				if (args.Count != 1)
				{
					return false;
				}
				double rad = args[0] * (Math.PI / 180.0);
				if (Math.Abs(Math.Cos(rad)) < 1e-9)
				{
					return false;
				}
				matrix = new Matrix(1.0, Math.Tan(rad), 0.0, 1.0, 0.0, 0.0);
				return IsTransformMatrixValid(matrix);
			}
			default:
				return false;
		}
	}

	private static bool IsTransformMatrixValid(Matrix m)
	{
		if (!double.IsFinite(m.M11) || !double.IsFinite(m.M12) ||
			!double.IsFinite(m.M21) || !double.IsFinite(m.M22) ||
			!double.IsFinite(m.OffsetX) || !double.IsFinite(m.OffsetY))
		{
			return false;
		}
		if (Math.Abs(m.M11) > MaxCoordinateAbs || Math.Abs(m.M12) > MaxCoordinateAbs ||
			Math.Abs(m.M21) > MaxCoordinateAbs || Math.Abs(m.M22) > MaxCoordinateAbs ||
			Math.Abs(m.OffsetX) > MaxCoordinateAbs || Math.Abs(m.OffsetY) > MaxCoordinateAbs)
		{
			return false;
		}
		double det = m.Determinant;
		return double.IsFinite(det) &&
			Math.Abs(det) >= MinMatrixDeterminant &&
			Math.Abs(det) <= MaxMatrixDeterminant;
	}

	private static bool TransformPathGeometry(
		PathGeometry source,
		Matrix matrix,
		out PathGeometry transformed,
		out string? errorCode)
	{
		errorCode = null;
		if (matrix.IsIdentity)
		{
			transformed = source;
			if (IsBoundsRunaway(transformed.Bounds))
			{
				errorCode = SvgIconReasonCodes.InvalidPathData;
				return false;
			}
			return true;
		}

		var result = new PathGeometry { FillRule = source.FillRule };
		foreach (PathFigure srcFig in source.Figures)
		{
			if (!TryTransformPoint(srcFig.StartPoint, matrix, out Point start, out errorCode))
			{
				transformed = result;
				return false;
			}

			var dstFig = new PathFigure
			{
				StartPoint = start,
				IsFilled = srcFig.IsFilled,
				IsClosed = srcFig.IsClosed
			};

			Point currentLocal = srcFig.StartPoint;
			foreach (PathSegment seg in srcFig.Segments)
			{
				switch (seg)
				{
					case LineSegment line:
					{
						if (!TryTransformPoint(line.Point, matrix, out Point p, out errorCode))
						{
							transformed = result;
							return false;
						}
						dstFig.Segments.Add(new LineSegment(p, line.IsStroked));
						currentLocal = line.Point;
						break;
					}
					case PolyLineSegment polyLine:
					{
						if (!TryTransformPoints(polyLine.Points, matrix, ref currentLocal, out PointCollection pts, out errorCode))
						{
							transformed = result;
							return false;
						}
						dstFig.Segments.Add(new PolyLineSegment(pts, polyLine.IsStroked));
						break;
					}
					case BezierSegment bezier:
					{
						if (!TryTransformPoint(bezier.Point1, matrix, out Point p1, out errorCode) ||
							!TryTransformPoint(bezier.Point2, matrix, out Point p2, out errorCode) ||
							!TryTransformPoint(bezier.Point3, matrix, out Point p3, out errorCode))
						{
							transformed = result;
							return false;
						}
						dstFig.Segments.Add(new BezierSegment(p1, p2, p3, bezier.IsStroked));
						currentLocal = bezier.Point3;
						break;
					}
					case PolyBezierSegment polyBezier:
					{
						if (!TryTransformPoints(polyBezier.Points, matrix, ref currentLocal, out PointCollection pts, out errorCode))
						{
							transformed = result;
							return false;
						}
						dstFig.Segments.Add(new PolyBezierSegment(pts, polyBezier.IsStroked));
						break;
					}
					case QuadraticBezierSegment quad:
					{
						if (!TryTransformPoint(quad.Point1, matrix, out Point p1, out errorCode) ||
							!TryTransformPoint(quad.Point2, matrix, out Point p2, out errorCode))
						{
							transformed = result;
							return false;
						}
						dstFig.Segments.Add(new QuadraticBezierSegment(p1, p2, quad.IsStroked));
						currentLocal = quad.Point2;
						break;
					}
					case PolyQuadraticBezierSegment polyQuad:
					{
						if (!TryTransformPoints(polyQuad.Points, matrix, ref currentLocal, out PointCollection pts, out errorCode))
						{
							transformed = result;
							return false;
						}
						dstFig.Segments.Add(new PolyQuadraticBezierSegment(pts, polyQuad.IsStroked));
						break;
					}
					case ArcSegment arc:
					{
						if (currentLocal != arc.Point)
						{
							if (Math.Abs(arc.Size.Width) < 1e-12 || Math.Abs(arc.Size.Height) < 1e-12)
							{
								if (!TryTransformPoint(arc.Point, matrix, out Point endPt, out errorCode))
								{
									transformed = result;
									return false;
								}
								dstFig.Segments.Add(new LineSegment(endPt, arc.IsStroked));
							}
							else
							{
								foreach ((Point c1, Point c2, Point end) in ConvertArcToCubicBeziers(
									currentLocal,
									arc.Point,
									arc.Size.Width,
									arc.Size.Height,
									arc.RotationAngle,
									arc.IsLargeArc,
									arc.SweepDirection))
								{
									if (!TryTransformPoint(c1, matrix, out Point tc1, out errorCode) ||
										!TryTransformPoint(c2, matrix, out Point tc2, out errorCode) ||
										!TryTransformPoint(end, matrix, out Point tend, out errorCode))
									{
										transformed = result;
										return false;
									}
									dstFig.Segments.Add(new BezierSegment(tc1, tc2, tend, arc.IsStroked));
								}
							}
						}
						currentLocal = arc.Point;
						break;
					}
					default:
						errorCode = SvgIconReasonCodes.InvalidPathData;
						transformed = result;
						return false;
				}
			}

			result.Figures.Add(dstFig);
		}

		if (IsBoundsRunaway(result.Bounds))
		{
			errorCode = !double.IsFinite(result.Bounds.X) || !double.IsFinite(result.Bounds.Y) ||
				!double.IsFinite(result.Bounds.Width) || !double.IsFinite(result.Bounds.Height)
				? SvgIconReasonCodes.NonFiniteCoordinate
				: SvgIconReasonCodes.TransformUnsupported;
			transformed = result;
			return false;
		}

		transformed = result;
		return true;
	}

	private static bool TryTransformPoint(Point local, Matrix matrix, out Point transformed, out string? errorCode)
	{
		transformed = matrix.Transform(local);
		if (!double.IsFinite(transformed.X) || !double.IsFinite(transformed.Y))
		{
			errorCode = SvgIconReasonCodes.NonFiniteCoordinate;
			return false;
		}
		if (Math.Abs(transformed.X) > MaxCoordinateAbs || Math.Abs(transformed.Y) > MaxCoordinateAbs)
		{
			errorCode = SvgIconReasonCodes.TransformUnsupported;
			return false;
		}
		errorCode = null;
		return true;
	}

	private static bool TryTransformPoints(
		PointCollection source,
		Matrix matrix,
		ref Point currentLocal,
		out PointCollection transformed,
		out string? errorCode)
	{
		transformed = new PointCollection(source.Count);
		errorCode = null;
		foreach (Point lp in source)
		{
			if (!TryTransformPoint(lp, matrix, out Point p, out errorCode))
			{
				return false;
			}
			transformed.Add(p);
			currentLocal = lp;
		}
		return true;
	}

	private static List<(Point c1, Point c2, Point end)> ConvertArcToCubicBeziers(
		Point p0,
		Point p1,
		double rx,
		double ry,
		double angleDeg,
		bool isLargeArc,
		SweepDirection sweep)
	{
		var result = new List<(Point, Point, Point)>();
		rx = Math.Abs(rx);
		ry = Math.Abs(ry);

		double phi = angleDeg * (Math.PI / 180.0);
		double cosPhi = Math.Cos(phi);
		double sinPhi = Math.Sin(phi);

		double dx2 = (p0.X - p1.X) * 0.5;
		double dy2 = (p0.Y - p1.Y) * 0.5;
		double x1p = cosPhi * dx2 + sinPhi * dy2;
		double y1p = -sinPhi * dx2 + cosPhi * dy2;

		double rxSq = rx * rx;
		double rySq = ry * ry;
		double x1pSq = x1p * x1p;
		double y1pSq = y1p * y1p;

		double lambda = x1pSq / rxSq + y1pSq / rySq;
		if (lambda > 1.0)
		{
			double scale = Math.Sqrt(lambda);
			rx *= scale;
			ry *= scale;
			rxSq = rx * rx;
			rySq = ry * ry;
		}

		bool sweepFlag = sweep == SweepDirection.Clockwise;
		double denom = rxSq * y1pSq + rySq * x1pSq;
		double num = Math.Max(0.0, rxSq * rySq - denom);
		double factor = denom > 0.0 ? Math.Sqrt(num / denom) : 0.0;
		if (isLargeArc == sweepFlag)
		{
			factor = -factor;
		}

		double cxp = factor * (rx * y1p / ry);
		double cyp = factor * (-ry * x1p / rx);
		double cx = cosPhi * cxp - sinPhi * cyp + (p0.X + p1.X) * 0.5;
		double cy = sinPhi * cxp + cosPhi * cyp + (p0.Y + p1.Y) * 0.5;

		static double VectorAngle(double ux, double uy, double vx, double vy)
		{
			double dot = ux * vx + uy * vy;
			double len = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
			double cos = len > 0.0 ? Math.Clamp(dot / len, -1.0, 1.0) : 1.0;
			double angle = Math.Acos(cos);
			return (ux * vy - uy * vx) < 0.0 ? -angle : angle;
		}

		double ux = (x1p - cxp) / rx;
		double uy = (y1p - cyp) / ry;
		double vx = (-x1p - cxp) / rx;
		double vy = (-y1p - cyp) / ry;

		double theta1 = VectorAngle(1.0, 0.0, ux, uy);
		double dTheta = VectorAngle(ux, uy, vx, vy);
		if (!sweepFlag && dTheta > 0.0)
		{
			dTheta -= 2.0 * Math.PI;
		}
		else if (sweepFlag && dTheta < 0.0)
		{
			dTheta += 2.0 * Math.PI;
		}

		int segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(dTheta) / (Math.PI * 0.5)));
		double delta = dTheta / segments;

		Point MapEllipsePoint(double ex, double ey)
		{
			double x = rx * ex;
			double y = ry * ey;
			return new Point(cx + cosPhi * x - sinPhi * y, cy + sinPhi * x + cosPhi * y);
		}

		for (int i = 0; i < segments; i++)
		{
			double t1 = theta1 + i * delta;
			double t2 = t1 + delta;
			double alpha = (4.0 / 3.0) * Math.Tan((t2 - t1) * 0.25);

			Point cp1 = MapEllipsePoint(Math.Cos(t1) - alpha * Math.Sin(t1), Math.Sin(t1) + alpha * Math.Cos(t1));
			Point cp2 = MapEllipsePoint(Math.Cos(t2) + alpha * Math.Sin(t2), Math.Sin(t2) - alpha * Math.Cos(t2));
			Point ep = i == segments - 1 ? p1 : MapEllipsePoint(Math.Cos(t2), Math.Sin(t2));
			result.Add((cp1, cp2, ep));
		}

		return result;
	}

	private static SvgPathValidationResult BuildCanonicalPathData(DocumentValidationState docState)
	{
		bool allRawEligible = docState.Elements.All(item => item.IsRawPathEligible && item.RawPathData != null);
		if (allRawEligible)
		{
			string combinedRaw = string.Join(" ", docState.Elements.Select(item => item.RawPathData!));
			if (IsRawConcatGeometricallyEquivalent(docState.Elements, combinedRaw))
			{
				if (Encoding.UTF8.GetByteCount(combinedRaw) > MaxSvgFileBytes)
				{
					return SvgPathValidationResult.Failure(SvgIconReasonCodes.FileTooLarge);
				}
				return ValidatePathData(combinedRaw);
			}
		}

		var areaComponents = new List<PathGeometry>();
		var openFallbackFigures = new List<PathFigure>();

		foreach (CollectedGeometry item in docState.Elements)
		{
			PathGeometry resolved = Geometry.Combine(item.Geometry, Geometry.Empty, GeometryCombineMode.Union, null);
			if (!IsBoundsEmptyOrZero(resolved.Bounds))
			{
				AddUnionComponent(areaComponents, resolved);
			}
			else if (!IsBoundsEmptyOrZero(item.Geometry.Bounds))
			{
				foreach (PathFigure figure in item.Geometry.Figures)
				{
					openFallbackFigures.Add(figure.Clone());
				}
			}
		}

		var canonical = new PathGeometry { FillRule = FillRule.EvenOdd };
		foreach (PathGeometry component in areaComponents)
		{
			foreach (PathFigure figure in component.Figures)
			{
				canonical.Figures.Add(figure.Clone());
			}
		}
		foreach (PathFigure openFigure in openFallbackFigures)
		{
			canonical.Figures.Add(openFigure);
		}

		if (canonical.Figures.Count == 0 || IsBoundsEmptyOrZero(canonical.Bounds))
		{
			return SvgPathValidationResult.Failure(SvgIconReasonCodes.EmptyGeometry);
		}
		if (IsBoundsRunaway(canonical.Bounds))
		{
			return SvgPathValidationResult.Failure(SvgIconReasonCodes.NonFiniteCoordinate);
		}

		string serialized = canonical.ToString(CultureInfo.InvariantCulture);
		if (string.IsNullOrWhiteSpace(serialized) ||
			!serialized.StartsWith("M", StringComparison.OrdinalIgnoreCase))
		{
			return SvgPathValidationResult.Failure(SvgIconReasonCodes.InvalidPathData);
		}
		if (Encoding.UTF8.GetByteCount(serialized) > MaxSvgFileBytes)
		{
			return SvgPathValidationResult.Failure(SvgIconReasonCodes.FileTooLarge);
		}

		return ValidatePathData(serialized);
	}

	private static bool IsRawConcatGeometricallyEquivalent(List<CollectedGeometry> elements, string combinedRaw)
	{
		try
		{
			Geometry naive = Geometry.Parse(combinedRaw);
			PathGeometry? painterUnion = null;
			foreach (CollectedGeometry element in elements)
			{
				PathGeometry resolved = Geometry.Combine(element.Geometry, Geometry.Empty, GeometryCombineMode.Union, null);
				if (!IsBoundsEmptyOrZero(resolved.Bounds))
				{
					painterUnion = painterUnion == null
						? resolved
						: Geometry.Combine(painterUnion, resolved, GeometryCombineMode.Union, null);
				}
			}
			if (painterUnion == null)
			{
				return true;
			}
			PathGeometry xor = Geometry.Combine(painterUnion, naive, GeometryCombineMode.Xor, null);
			return xor.IsEmpty() || IsBoundsEmptyOrZero(xor.Bounds);
		}
		catch
		{
			return false;
		}
	}

	private static void AddUnionComponent(List<PathGeometry> components, PathGeometry next)
	{
		PathGeometry current = next;
		bool merged;
		do
		{
			merged = false;
			Rect bounds = current.Bounds;
			for (int i = components.Count - 1; i >= 0; i--)
			{
				if (bounds.IntersectsWith(components[i].Bounds))
				{
					current = Geometry.Combine(components[i], current, GeometryCombineMode.Union, null);
					components.RemoveAt(i);
					merged = true;
				}
			}
		}
		while (merged);

		if (!IsBoundsEmptyOrZero(current.Bounds))
		{
			components.Add(current);
		}
	}

	private static bool IsBoundsEmptyOrZero(Rect bounds)
	{
		return bounds.IsEmpty || (bounds.Width <= 0.0 && bounds.Height <= 0.0);
	}

	private static bool IsBoundsRunaway(Rect bounds)
	{
		if (bounds.IsEmpty)
		{
			return false;
		}
		return !double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) ||
			!double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) ||
			Math.Abs(bounds.X) > MaxCoordinateAbs || Math.Abs(bounds.Y) > MaxCoordinateAbs ||
			bounds.Width > MaxGeometryDimension || bounds.Height > MaxGeometryDimension;
	}

	private static int CountSegments(PathGeometry pg)
	{
		int count = 0;
		foreach (PathFigure fig in pg.Figures)
		{
			count += fig.Segments.Count;
		}
		return count;
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
			if (IsBoundsEmptyOrZero(geometry.Bounds))
			{
				return SvgPathValidationResult.Failure(SvgIconReasonCodes.EmptyGeometry);
			}
			if (IsBoundsRunaway(geometry.Bounds))
			{
				return SvgPathValidationResult.Failure(SvgIconReasonCodes.NonFiniteCoordinate);
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

	private enum ElementKind
	{
		Svg,
		Group,
		Path,
		Circle,
		Ellipse,
		Rect,
		Line,
		Polygon,
		Polyline,
		MetadataText
	}

	private readonly record struct ElementRenderState(
		string Fill,
		FillRule FillRule,
		string Stroke,
		double StrokeWidth,
		PenLineCap StrokeLineCap,
		PenLineJoin StrokeLineJoin,
		double StrokeMiterLimit,
		Matrix Transform)
	{
		public static ElementRenderState CreateRootDefault()
			=> new("#FF000000", FillRule.Nonzero, "none", 1.0, PenLineCap.Flat, PenLineJoin.Miter, 4.0, Matrix.Identity);
	}

	private sealed record CollectedGeometry(
		PathGeometry Geometry,
		string? RawPathData,
		bool IsRawPathEligible);

	private sealed class DocumentValidationState
	{
		public List<CollectedGeometry> Elements { get; } = new();
		public HashSet<string> PaintedColors { get; } = new(StringComparer.OrdinalIgnoreCase);
		public bool SawInvisibleGeometry { get; set; }
		public int DrawableCount { get; set; }
		public int TotalSegmentCount { get; set; }
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
