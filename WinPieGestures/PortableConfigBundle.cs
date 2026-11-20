using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WinPieGestures;

public static class PortableConfigBundleReasonCodes
{
	public const string InvalidArgument = "INVALID_ARGUMENT";
	public const string ConfigInvalid = "CONFIG_INVALID";
	public const string ConfigTooLarge = "CONFIG_TOO_LARGE";
	public const string IconDirectoryInvalid = "ICON_DIRECTORY_INVALID";
	public const string IconMissing = "ICON_MISSING";
	public const string IconBitmapUnsupported = "ICON_BITMAP_UNSUPPORTED";
	public const string IconAmbiguous = "ICON_AMBIGUOUS";
	public const string IconTooLarge = "ICON_TOO_LARGE";
	public const string SvgInvalid = "SVG_INVALID";
	public const string AssetCountLimit = "ASSET_COUNT_LIMIT";
	public const string AssetSizeLimit = "ASSET_SIZE_LIMIT";
	public const string ResourceSizeLimit = "RESOURCE_SIZE_LIMIT";
	public const string ManifestInvalid = "MANIFEST_INVALID";
	public const string ManifestTooLarge = "MANIFEST_TOO_LARGE";
	public const string UnsupportedFormat = "UNSUPPORTED_FORMAT";
	public const string UnsupportedVersion = "UNSUPPORTED_VERSION";
	public const string ZipInvalid = "ZIP_INVALID";
	public const string EntryInvalid = "ENTRY_INVALID";
	public const string DuplicateEntry = "DUPLICATE_ENTRY";
	public const string ExtraEntry = "EXTRA_ENTRY";
	public const string MissingEntry = "MISSING_ENTRY";
	public const string SizeMismatch = "SIZE_MISMATCH";
	public const string HashMismatch = "HASH_MISMATCH";
	public const string AssetMissing = "ASSET_MISSING";
	public const string AssetUnused = "ASSET_UNUSED";
	public const string OutputInvalid = "OUTPUT_INVALID";
	public const string IoError = "IO_ERROR";
}

public sealed class PortableConfigBundleException : Exception
{
	public string ReasonCode { get; }

	public PortableConfigBundleException(string reasonCode, string message, Exception? innerException = null)
		: base(message, innerException)
	{
		ReasonCode = reasonCode;
	}
}

public sealed class PortableConfigImportTransaction : IDisposable
{
	private readonly List<string> _createdFiles;
	private readonly bool _createdDirectory;
	private readonly string _iconDirectory;
	private bool _committed;
	private bool _disposed;

	internal PortableConfigImportTransaction(
		string configJson,
		List<string> createdFiles,
		bool createdDirectory,
		string iconDirectory)
	{
		ConfigJson = configJson;
		_createdFiles = createdFiles;
		_createdDirectory = createdDirectory;
		_iconDirectory = iconDirectory;
	}

	public string ConfigJson { get; }

	public void Commit()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(PortableConfigImportTransaction));
		}
		_committed = true;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		if (!_committed)
		{
			RollbackCreatedResources();
		}
	}

	private void RollbackCreatedResources()
	{
		bool removed = false;
		for (int index = _createdFiles.Count - 1; index >= 0; index--)
		{
			try
			{
				if (File.Exists(_createdFiles[index]))
				{
					File.Delete(_createdFiles[index]);
					removed = true;
				}
			}
			catch
			{
			}
		}
		if (removed)
		{
			try
			{
				IconHelper.InvalidateCustomIconCache();
			}
			catch
			{
			}
		}
		if (_createdDirectory)
		{
			try
			{
				if (Directory.Exists(_iconDirectory) && !Directory.EnumerateFileSystemEntries(_iconDirectory).Any())
				{
					Directory.Delete(_iconDirectory);
				}
			}
			catch
			{
			}
		}
	}
}

public static class PortableConfigBundle
{
	public const string Format = "StarPiePortableConfig";
	public const int Version = 1;
	public const long MaxConfigBytes = 16L * 1024L * 1024L;
	public const long MaxManifestBytes = 1L * 1024L * 1024L;
	public const long MaxAssetBytes = SvgIconBatchImporter.MaxSvgFileBytes;
	public const long MaxTotalAssetBytes = 64L * 1024L * 1024L;
	public const int MaxAssetCount = 512;

	private const string ManifestEntryName = "manifest.json";
	private const string ConfigEntryName = "config.json";
	private const string CustomPrefix = "custom:";
	private static readonly UTF8Encoding StrictUtf8 = new(false, true);
	private static readonly UTF8Encoding Utf8WithoutBom = new(false);
	private static readonly JsonSerializerOptions CompactJsonOptions = new()
	{
		WriteIndented = false,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};
	private static readonly HashSet<string> BitmapExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".png", ".jpg", ".jpeg", ".ico", ".bmp", ".webp"
	};

	public static void Export(string configJson, string targetPath, string customIconsDirectory)
	{
		if (configJson == null)
		{
			throw Fail(PortableConfigBundleReasonCodes.InvalidArgument, "configJson is required.");
		}
		if (string.IsNullOrWhiteSpace(targetPath))
		{
			throw Fail(PortableConfigBundleReasonCodes.InvalidArgument, "targetPath is required.");
		}
		if (string.IsNullOrWhiteSpace(customIconsDirectory))
		{
			throw Fail(PortableConfigBundleReasonCodes.InvalidArgument, "customIconsDirectory is required.");
		}

		byte[] configBytes = EncodeUtf8(configJson, PortableConfigBundleReasonCodes.ConfigInvalid);
		if (configBytes.LongLength > MaxConfigBytes)
		{
			throw Fail(PortableConfigBundleReasonCodes.ConfigTooLarge, "The configuration exceeds the 16 MiB limit.");
		}

		JsonObject configRoot = ParseConfigObject(configJson);
		List<string> referencedKeys = CollectCustomIconKeys(configRoot);
		List<AssetSource> assets = ResolveAssetSources(referencedKeys, customIconsDirectory);
		ValidateSvgAssets(assets);

		string targetFullPath;
		try
		{
			targetFullPath = Path.GetFullPath(targetPath);
		}
		catch (Exception ex)
		{
			throw Fail(PortableConfigBundleReasonCodes.OutputInvalid, "The export target path is invalid.", ex);
		}
		if (Directory.Exists(targetFullPath))
		{
			throw Fail(PortableConfigBundleReasonCodes.OutputInvalid, "The export target is a directory.");
		}

		string targetDirectory = Path.GetDirectoryName(targetFullPath) ?? "";
		if (targetDirectory.Length == 0)
		{
			throw Fail(PortableConfigBundleReasonCodes.OutputInvalid, "The export target has no parent directory.");
		}

		string temporaryPath = "";
		try
		{
			Directory.CreateDirectory(targetDirectory);
			temporaryPath = Path.Combine(
				targetDirectory,
				"." + Path.GetFileName(targetFullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");

			byte[] manifestBytes = BuildManifest(assets);
			if (manifestBytes.LongLength > MaxManifestBytes)
			{
				throw Fail(PortableConfigBundleReasonCodes.ManifestTooLarge, "The generated manifest is too large.");
			}

			using (var file = new FileStream(
				temporaryPath,
				FileMode.CreateNew,
				FileAccess.ReadWrite,
				FileShare.None,
				64 * 1024,
				FileOptions.SequentialScan))
			{
				using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
				{
					WriteZipEntry(archive, ManifestEntryName, manifestBytes);
					WriteZipEntry(archive, ConfigEntryName, configBytes);
					foreach (AssetSource asset in assets)
					{
						WriteZipEntry(archive, asset.EntryName, asset.Data);
					}
				}
				file.Flush(true);
			}

			File.Move(temporaryPath, targetFullPath, overwrite: true);
			temporaryPath = "";
		}
		catch (PortableConfigBundleException)
		{
			throw;
		}
		catch (Exception ex)
		{
			throw Fail(PortableConfigBundleReasonCodes.IoError, "Unable to create the portable configuration bundle.", ex);
		}
		finally
		{
			if (temporaryPath.Length > 0)
			{
				TryDeleteFile(temporaryPath);
			}
		}
	}

	public static PortableConfigImportTransaction PrepareImport(string packagePath, string customIconsDirectory)
	{
		if (string.IsNullOrWhiteSpace(packagePath))
		{
			throw Fail(PortableConfigBundleReasonCodes.InvalidArgument, "packagePath is required.");
		}
		if (string.IsNullOrWhiteSpace(customIconsDirectory))
		{
			throw Fail(PortableConfigBundleReasonCodes.InvalidArgument, "customIconsDirectory is required.");
		}

		string packageFullPath;
		string iconDirectory;
		try
		{
			packageFullPath = Path.GetFullPath(packagePath);
			iconDirectory = Path.GetFullPath(customIconsDirectory);
		}
		catch (Exception ex)
		{
			throw Fail(PortableConfigBundleReasonCodes.InvalidArgument, "The package or icon directory path is invalid.", ex);
		}

		if (!File.Exists(packageFullPath))
		{
			throw Fail(PortableConfigBundleReasonCodes.MissingEntry, "The portable configuration bundle does not exist.");
		}

		PackageContents package;
		try
		{
			using var file = new FileStream(
				packageFullPath,
				FileMode.Open,
				FileAccess.Read,
				FileShare.Read,
				64 * 1024,
				FileOptions.SequentialScan);
			using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
			package = ReadPackage(archive);
		}
		catch (PortableConfigBundleException)
		{
			throw;
		}
		catch (Exception ex)
		{
			throw Fail(PortableConfigBundleReasonCodes.ZipInvalid, "Unable to read the portable configuration bundle.", ex);
		}

		ValidateSvgAssets(package.Assets.Select(asset => new AssetSource(asset.Key, asset.EntryName, asset.Data)).ToList());
		List<string> referencedKeys = CollectCustomIconKeys(package.ConfigRoot);
		ValidateAssetReferences(referencedKeys, package.Assets);

		bool createdDirectory = !Directory.Exists(iconDirectory);
		var createdFiles = new List<string>();
		try
		{
			Directory.CreateDirectory(iconDirectory);
			var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (AssetPayload asset in package.Assets)
			{
				string newKey = CreateUniqueAsset(iconDirectory, asset.Key, asset.Data, createdFiles);
				replacements.Add(asset.Key, newKey);
			}

			string configJson = RemapConfigJsonPreservingLayout(
				package.ConfigJson,
				package.ConfigRoot,
				replacements);
			byte[] configBytes = EncodeUtf8(configJson, PortableConfigBundleReasonCodes.ConfigInvalid);
			if (configBytes.LongLength > MaxConfigBytes)
			{
				throw Fail(PortableConfigBundleReasonCodes.ConfigTooLarge, "The remapped configuration exceeds the 16 MiB limit.");
			}

			if (createdFiles.Count > 0)
			{
				IconHelper.InvalidateCustomIconCache();
			}
			return new PortableConfigImportTransaction(configJson, createdFiles, createdDirectory, iconDirectory);
		}
		catch (PortableConfigBundleException)
		{
			RollbackFiles(createdFiles);
			if (createdDirectory)
			{
				TryDeleteEmptyDirectory(iconDirectory);
			}
			throw;
		}
		catch (Exception ex)
		{
			RollbackFiles(createdFiles);
			if (createdDirectory)
			{
				TryDeleteEmptyDirectory(iconDirectory);
			}
			throw Fail(PortableConfigBundleReasonCodes.IoError, "Unable to prepare the imported configuration resources.", ex);
		}
	}

	private static JsonObject ParseConfigObject(string json)
	{
		try
		{
			JsonNode? node = JsonNode.Parse(
				json,
				new JsonNodeOptions { PropertyNameCaseInsensitive = false },
				new JsonDocumentOptions
				{
					AllowTrailingCommas = false,
					CommentHandling = JsonCommentHandling.Disallow,
					MaxDepth = 64
				});
			if (node is JsonObject root)
			{
				return root;
			}
			throw Fail(PortableConfigBundleReasonCodes.ConfigInvalid, "The configuration root must be a JSON object.");
		}
		catch (PortableConfigBundleException)
		{
			throw;
		}
		catch (JsonException ex)
		{
			throw Fail(PortableConfigBundleReasonCodes.ConfigInvalid, "The configuration JSON is invalid.", ex);
		}
	}

	private static bool TryGetKnownProperty(
		JsonObject obj,
		string propertyName,
		out string actualPropertyName,
		out JsonNode? value)
	{
		actualPropertyName = "";
		value = null;
		bool found = false;
		foreach (KeyValuePair<string, JsonNode?> property in obj)
		{
			if (!string.Equals(property.Key, propertyName, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			if (found)
			{
				throw Fail(
					PortableConfigBundleReasonCodes.ConfigInvalid,
					"The configuration contains case-equivalent duplicate known properties.");
			}
			found = true;
			actualPropertyName = property.Key;
			value = property.Value;
		}
		return found;
	}

	private static void ValidateKnownProperties(JsonObject obj, params string[] propertyNames)
	{
		foreach (string propertyName in propertyNames)
		{
			_ = TryGetKnownProperty(obj, propertyName, out _, out _);
		}
	}

	private static List<string> CollectCustomIconKeys(JsonObject root)
	{
		var keys = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		void AddValue(JsonNode? node)
		{
			if (node is not JsonValue value || !value.TryGetValue<string>(out string? text) || text == null)
			{
				return;
			}
			if (text.StartsWith(CustomPrefix, StringComparison.OrdinalIgnoreCase) && seen.Add(text))
			{
				keys.Add(text);
			}
		}

		void AddAction(JsonNode? node)
		{
			if (node is not JsonObject action)
			{
				return;
			}
			ValidateKnownProperties(action, "IconKey", "SubActions");
			if (TryGetKnownProperty(action, "IconKey", out _, out JsonNode? iconNode))
			{
				AddValue(iconNode);
			}
			if (TryGetKnownProperty(action, "SubActions", out _, out JsonNode? subActionsNode) &&
				subActionsNode is JsonArray subActions)
			{
				foreach (JsonNode? subAction in subActions)
				{
					AddAction(subAction);
				}
			}
		}

		void AddActions(JsonNode? node)
		{
			if (node is JsonArray actions)
			{
				foreach (JsonNode? action in actions)
				{
					AddAction(action);
				}
			}
		}

		ValidateKnownProperties(
			root,
			"ActionPanels",
			"Profiles",
			"CancelAction",
			"CoreCustomIconKey",
			"GestureMappings");

		if (TryGetKnownProperty(root, "ActionPanels", out _, out JsonNode? panelsNode) &&
			panelsNode is JsonArray panels)
		{
			foreach (JsonNode? panelNode in panels)
			{
				if (panelNode is JsonObject panel)
				{
					ValidateKnownProperties(panel, "Keys");
					if (TryGetKnownProperty(panel, "Keys", out _, out JsonNode? keysNode))
					{
						AddActions(keysNode);
					}
				}
			}
		}

		if (TryGetKnownProperty(root, "Profiles", out _, out JsonNode? profilesNode) &&
			profilesNode is JsonArray profiles)
		{
			foreach (JsonNode? profileNode in profiles)
			{
				if (profileNode is not JsonObject profile)
				{
					continue;
				}
				ValidateKnownProperties(profile, "Actions", "CenterAction", "Layers");
				if (TryGetKnownProperty(profile, "Actions", out _, out JsonNode? actionsNode))
				{
					AddActions(actionsNode);
				}
				if (TryGetKnownProperty(profile, "CenterAction", out _, out JsonNode? centerActionNode))
				{
					AddAction(centerActionNode);
				}
				if (TryGetKnownProperty(profile, "Layers", out _, out JsonNode? layersNode) &&
					layersNode is JsonArray layers)
				{
					foreach (JsonNode? layerNode in layers)
					{
						if (layerNode is not JsonObject layer)
						{
							continue;
						}
						ValidateKnownProperties(layer, "Actions", "CenterAction");
						if (TryGetKnownProperty(layer, "Actions", out _, out JsonNode? layerActionsNode))
						{
							AddActions(layerActionsNode);
						}
						if (TryGetKnownProperty(layer, "CenterAction", out _, out JsonNode? layerCenterActionNode))
						{
							AddAction(layerCenterActionNode);
						}
					}
				}
			}
		}

		if (TryGetKnownProperty(root, "GestureMappings", out _, out JsonNode? gestureMappingsNode) &&
			gestureMappingsNode is JsonArray gestureMappings)
		{
			foreach (JsonNode? mappingNode in gestureMappings)
			{
				if (mappingNode is not JsonObject mapping)
				{
					continue;
				}
				ValidateKnownProperties(mapping, "Action");
				if (TryGetKnownProperty(mapping, "Action", out _, out JsonNode? actionNode))
				{
					AddAction(actionNode);
				}
			}
		}

		if (TryGetKnownProperty(root, "CancelAction", out _, out JsonNode? cancelActionNode))
		{
			AddAction(cancelActionNode);
		}
		if (TryGetKnownProperty(root, "CoreCustomIconKey", out _, out JsonNode? coreIconNode))
		{
			AddValue(coreIconNode);
		}
		return keys;
	}

	private static string RemapConfigJsonPreservingLayout(
		string configJson,
		JsonObject root,
		IReadOnlyDictionary<string, string> replacements)
	{
		if (replacements.Count == 0)
		{
			return configJson;
		}

		var knownPaths = new HashSet<string>(StringComparer.Ordinal);
		CollectRemapPaths(root, knownPaths);
		byte[] input = EncodeUtf8(configJson, PortableConfigBundleReasonCodes.ConfigInvalid);
		byte[] output;
		try
		{
			output = RewriteKnownJsonStrings(input, knownPaths, replacements);
		}
		catch (JsonException ex)
		{
			throw Fail(PortableConfigBundleReasonCodes.ConfigInvalid, "The configuration JSON is invalid.", ex);
		}
		try
		{
			return StrictUtf8.GetString(output);
		}
		catch (DecoderFallbackException ex)
		{
			throw Fail(PortableConfigBundleReasonCodes.ConfigInvalid, "The remapped configuration is not valid UTF-8.", ex);
		}
	}

	private static void CollectRemapPaths(JsonObject root, HashSet<string> paths)
	{
		void AddAction(string path, JsonNode? node)
		{
			if (node is not JsonObject action)
			{
				return;
			}
			ValidateKnownProperties(action, "IconKey", "SubActions");
			if (TryGetKnownProperty(action, "IconKey", out string iconPropertyName, out _))
			{
				paths.Add(path + "." + iconPropertyName);
			}
			if (TryGetKnownProperty(action, "SubActions", out string subActionsPropertyName, out JsonNode? subActionsNode) &&
				subActionsNode is JsonArray subActions)
			{
				for (int index = 0; index < subActions.Count; index++)
				{
					AddAction(
						path + "." + subActionsPropertyName + "[" + index.ToString() + "]",
						subActions[index]);
				}
			}
		}

		void AddActions(string path, JsonNode? node)
		{
			if (node is not JsonArray actions)
			{
				return;
			}
			for (int index = 0; index < actions.Count; index++)
			{
				AddAction(path + "[" + index.ToString() + "]", actions[index]);
			}
		}

		ValidateKnownProperties(
			root,
			"ActionPanels",
			"Profiles",
			"CancelAction",
			"CoreCustomIconKey",
			"GestureMappings");

		if (TryGetKnownProperty(root, "ActionPanels", out string panelsPropertyName, out JsonNode? panelsNode) &&
			panelsNode is JsonArray panels)
		{
			string panelsPath = "$." + panelsPropertyName;
			for (int panelIndex = 0; panelIndex < panels.Count; panelIndex++)
			{
				if (panels[panelIndex] is JsonObject panel)
				{
					ValidateKnownProperties(panel, "Keys");
					if (TryGetKnownProperty(panel, "Keys", out string keysPropertyName, out JsonNode? keysNode))
					{
						AddActions(
							panelsPath + "[" + panelIndex.ToString() + "]." + keysPropertyName,
							keysNode);
					}
				}
			}
		}

		if (TryGetKnownProperty(root, "Profiles", out string profilesPropertyName, out JsonNode? profilesNode) &&
			profilesNode is JsonArray profiles)
		{
			string profilesPath = "$." + profilesPropertyName;
			for (int profileIndex = 0; profileIndex < profiles.Count; profileIndex++)
			{
				if (profiles[profileIndex] is not JsonObject profile)
				{
					continue;
				}
				ValidateKnownProperties(profile, "Actions", "CenterAction", "Layers");
				string profilePath = profilesPath + "[" + profileIndex.ToString() + "]";
				if (TryGetKnownProperty(profile, "Actions", out string actionsPropertyName, out JsonNode? actionsNode))
				{
					AddActions(profilePath + "." + actionsPropertyName, actionsNode);
				}
				if (TryGetKnownProperty(profile, "CenterAction", out string centerActionPropertyName, out JsonNode? centerActionNode))
				{
					AddAction(profilePath + "." + centerActionPropertyName, centerActionNode);
				}
				if (TryGetKnownProperty(profile, "Layers", out string layersPropertyName, out JsonNode? layersNode) &&
					layersNode is JsonArray layers)
				{
					string layersPath = profilePath + "." + layersPropertyName;
					for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
					{
						if (layers[layerIndex] is not JsonObject layer)
						{
							continue;
						}
						ValidateKnownProperties(layer, "Actions", "CenterAction");
						string layerPath = layersPath + "[" + layerIndex.ToString() + "]";
						if (TryGetKnownProperty(layer, "Actions", out string layerActionsPropertyName, out JsonNode? layerActionsNode))
						{
							AddActions(layerPath + "." + layerActionsPropertyName, layerActionsNode);
						}
						if (TryGetKnownProperty(layer, "CenterAction", out string layerCenterActionPropertyName, out JsonNode? layerCenterActionNode))
						{
							AddAction(layerPath + "." + layerCenterActionPropertyName, layerCenterActionNode);
						}
					}
				}
			}
		}

		if (TryGetKnownProperty(root, "GestureMappings", out string gestureMappingsPropertyName, out JsonNode? gestureMappingsNode) &&
			gestureMappingsNode is JsonArray gestureMappings)
		{
			string gestureMappingsPath = "$." + gestureMappingsPropertyName;
			for (int mappingIndex = 0; mappingIndex < gestureMappings.Count; mappingIndex++)
			{
				if (gestureMappings[mappingIndex] is not JsonObject mapping)
				{
					continue;
				}
				ValidateKnownProperties(mapping, "Action");
				if (TryGetKnownProperty(mapping, "Action", out string actionPropertyName, out JsonNode? actionNode))
				{
					AddAction(
						gestureMappingsPath + "[" + mappingIndex.ToString() + "]." + actionPropertyName,
						actionNode);
				}
			}
		}

		if (TryGetKnownProperty(root, "CancelAction", out string cancelActionPropertyName, out JsonNode? cancelActionNode))
		{
			AddAction("$." + cancelActionPropertyName, cancelActionNode);
		}
		if (TryGetKnownProperty(root, "CoreCustomIconKey", out string corePropertyName, out _))
		{
			paths.Add("$." + corePropertyName);
		}
	}

	private static byte[] RewriteKnownJsonStrings(
		byte[] input,
		HashSet<string> knownPaths,
		IReadOnlyDictionary<string, string> replacements)
	{
		var reader = new Utf8JsonReader(
			input,
			new JsonReaderOptions
			{
				AllowTrailingCommas = false,
				CommentHandling = JsonCommentHandling.Disallow,
				MaxDepth = 64
			});
		var frames = new List<JsonFrame>();
		var output = new MemoryStream(input.Length);
		int copiedThrough = 0;
		while (reader.Read())
		{
			if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
			{
				PathSegment segment = frames.Count == 0
					? PathSegment.Root
					: ConsumeParentValue(frames);
				frames.Add(new JsonFrame(
					reader.TokenType == JsonTokenType.StartObject ? JsonFrameKind.Object : JsonFrameKind.Array,
					segment));
				continue;
			}
			if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
			{
				if (frames.Count == 0)
				{
					throw new JsonException("Unexpected JSON container end.");
				}
				frames.RemoveAt(frames.Count - 1);
				continue;
			}
			if (reader.TokenType == JsonTokenType.PropertyName)
			{
				if (frames.Count == 0 || frames[^1].Kind != JsonFrameKind.Object)
				{
					throw new JsonException("Unexpected JSON property.");
				}
				frames[^1].PendingProperty = reader.GetString() ?? "";
				continue;
			}

			PathSegment valueSegment = frames.Count == 0 ? PathSegment.Root : ConsumeParentValue(frames);
			string path = BuildJsonPath(frames, valueSegment);
			if (reader.TokenType == JsonTokenType.String &&
				knownPaths.Contains(path) &&
				reader.GetString() is string value &&
				replacements.TryGetValue(value, out string? replacement))
			{
				int start = checked((int)reader.TokenStartIndex);
				int end = checked((int)reader.BytesConsumed);
				output.Write(input, copiedThrough, start - copiedThrough);
				byte[] replacementBytes = JsonSerializer.SerializeToUtf8Bytes(replacement, CompactJsonOptions);
				output.Write(replacementBytes, 0, replacementBytes.Length);
				copiedThrough = end;
			}
		}
		output.Write(input, copiedThrough, input.Length - copiedThrough);
		return output.ToArray();
	}

	private static PathSegment ConsumeParentValue(List<JsonFrame> frames)
	{
		if (frames.Count == 0)
		{
			return PathSegment.Root;
		}
		JsonFrame parent = frames[^1];
		if (parent.Kind == JsonFrameKind.Object)
		{
			string property = parent.PendingProperty ?? "";
			parent.PendingProperty = null;
			return PathSegment.ForProperty(property);
		}
		int index = parent.NextArrayIndex++;
		return PathSegment.ForIndex(index);
	}

	private static string BuildJsonPath(IReadOnlyList<JsonFrame> frames, PathSegment valueSegment)
	{
		var builder = new StringBuilder("$");
		for (int index = 1; index < frames.Count; index++)
		{
			AppendPathSegment(builder, frames[index].Segment);
		}
		AppendPathSegment(builder, valueSegment);
		return builder.ToString();
	}

	private static void AppendPathSegment(StringBuilder builder, PathSegment segment)
	{
		if (segment.IsProperty)
		{
			builder.Append('.').Append(segment.Property);
		}
		else if (segment.IsIndex)
		{
			builder.Append('[').Append(segment.Index.ToString()).Append(']');
		}
	}

	private readonly struct PathSegment
	{
		private PathSegment(bool isProperty, string property, bool isIndex, int index)
		{
			IsProperty = isProperty;
			Property = property;
			IsIndex = isIndex;
			Index = index;
		}

		public static PathSegment Root => new(false, "", false, 0);
		public bool IsProperty { get; }
		public string Property { get; }
		public bool IsIndex { get; }
		public int Index { get; }

		public static PathSegment ForProperty(string property) => new(true, property, false, 0);
		public static PathSegment ForIndex(int index) => new(false, "", true, index);
	}

	private enum JsonFrameKind
	{
		Object,
		Array
	}

	private sealed class JsonFrame
	{
		public JsonFrame(JsonFrameKind kind, PathSegment segment)
		{
			Kind = kind;
			Segment = segment;
		}

		public JsonFrameKind Kind { get; }
		public PathSegment Segment { get; }
		public string? PendingProperty { get; set; }
		public int NextArrayIndex { get; set; }
	}

	private static List<AssetSource> ResolveAssetSources(List<string> keys, string customIconsDirectory)
	{
		if (keys.Count == 0)
		{
			return new List<AssetSource>();
		}
		if (!Directory.Exists(customIconsDirectory))
		{
			throw Fail(PortableConfigBundleReasonCodes.IconDirectoryInvalid, "The custom icon directory does not exist.");
		}

		Dictionary<string, List<string>> filesByStem;
		try
		{
			filesByStem = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			foreach (string filePath in Directory.EnumerateFiles(customIconsDirectory, "*", SearchOption.TopDirectoryOnly))
			{
				string stem = Path.GetFileNameWithoutExtension(filePath) ?? "";
				if (!filesByStem.TryGetValue(stem, out List<string>? matches))
				{
					matches = new List<string>();
					filesByStem.Add(stem, matches);
				}
				matches.Add(filePath);
			}
		}
		catch (Exception ex)
		{
			throw Fail(PortableConfigBundleReasonCodes.IconDirectoryInvalid, "Unable to enumerate the custom icon directory.", ex);
		}

		var assets = new List<AssetSource>();
		foreach (string key in keys)
		{
			string stem = key.Substring(CustomPrefix.Length);
			if (stem.Length == 0 || stem.IndexOfAny(new[] { '/', '\\', '\0' }) >= 0)
			{
				throw Fail(PortableConfigBundleReasonCodes.IconMissing, "The custom icon key contains an invalid file stem.");
			}
			if (!filesByStem.TryGetValue(stem, out List<string>? matches) || matches.Count == 0)
			{
				throw Fail(PortableConfigBundleReasonCodes.IconMissing, "A referenced custom icon is missing.");
			}
			if (matches.Count != 1)
			{
				throw Fail(PortableConfigBundleReasonCodes.IconAmbiguous, "A referenced custom icon has multiple files with the same stem.");
			}

			string sourcePath = matches[0];
			string extension = Path.GetExtension(sourcePath);
			if (BitmapExtensions.Contains(extension))
			{
				throw Fail(PortableConfigBundleReasonCodes.IconBitmapUnsupported, "Portable bundles only support SVG custom icons.");
			}
			if (!string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase))
			{
				throw Fail(PortableConfigBundleReasonCodes.IconMissing, "The referenced custom icon is not an SVG file.");
			}

			byte[] data = ReadFileBounded(sourcePath, MaxAssetBytes, PortableConfigBundleReasonCodes.IconTooLarge);
			assets.Add(new AssetSource(key, "", data));
		}
		return assets
			.OrderBy(asset => asset.Key, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static void ValidateSvgAssets(IReadOnlyList<AssetSource> assets)
	{
		if (assets.Count == 0)
		{
			return;
		}
		if (assets.Count > MaxAssetCount)
		{
			throw Fail(PortableConfigBundleReasonCodes.AssetCountLimit, "The bundle contains too many assets.");
		}
		long total = 0;
		foreach (AssetSource asset in assets)
		{
			if (asset.Data.LongLength > MaxAssetBytes)
			{
				throw Fail(PortableConfigBundleReasonCodes.AssetSizeLimit, "An SVG asset exceeds the 2 MiB limit.");
			}
			if (total > MaxTotalAssetBytes - asset.Data.LongLength)
			{
				throw Fail(PortableConfigBundleReasonCodes.ResourceSizeLimit, "The SVG assets exceed the 64 MiB total limit.");
			}
			total += asset.Data.LongLength;
		}

		string validationRoot = Path.Combine(
			Path.GetTempPath(),
			"StarPiePortableConfig_" + Guid.NewGuid().ToString("N"));
		try
		{
			Directory.CreateDirectory(validationRoot);
			string canonicalDir = Path.Combine(validationRoot, "canonical");
			var xmlPaths = new List<string>();
			for (int index = 0; index < assets.Count; index++)
			{
				if (TryExtractCanonicalBarePath(assets[index].Data, out string barePath))
				{
					SvgIconBatchImportResult bareResult = SvgIconBatchImporter.ImportBarePath(
						barePath,
						"asset_" + index.ToString(),
						canonicalDir,
						() => { },
						CancellationToken.None);
					if (bareResult.Cancelled || bareResult.SuccessCount != 1)
					{
						SvgIconBatchItemResult? failedBare = bareResult.Items.FirstOrDefault(item =>
							item.Status != SvgIconBatchItemStatus.Success);
						throw Fail(
							PortableConfigBundleReasonCodes.SvgInvalid,
							"An SVG asset is invalid" + (failedBare == null ? "." : ": " + failedBare.ReasonCode + "."));
					}
					continue;
				}

				string path = Path.Combine(validationRoot, "asset_" + index.ToString() + ".svg");
				File.WriteAllBytes(path, assets[index].Data);
				xmlPaths.Add(path);
			}

			if (xmlPaths.Count > 0)
			{
				SvgIconBatchImportResult result = SvgIconBatchImporter.ImportFiles(
					xmlPaths,
					canonicalDir,
					() => { },
					CancellationToken.None);
				if (result.Cancelled || result.SuccessCount != xmlPaths.Count)
				{
					SvgIconBatchItemResult? failed = result.Items.FirstOrDefault(item =>
						item.Status != SvgIconBatchItemStatus.Success);
					throw Fail(
						PortableConfigBundleReasonCodes.SvgInvalid,
						"An SVG asset is invalid" + (failed == null ? "." : ": " + failed.ReasonCode + "."));
				}
			}
		}
		catch (PortableConfigBundleException)
		{
			throw;
		}
		catch (Exception ex)
		{
			throw Fail(PortableConfigBundleReasonCodes.SvgInvalid, "Unable to validate an SVG asset.", ex);
		}
		finally
		{
			TryDeleteDirectory(validationRoot);
		}
	}

	private static bool TryExtractCanonicalBarePath(byte[] data, out string barePath)
	{
		barePath = "";
		if (data.Length == 0)
		{
			return false;
		}
		int offset = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
		string text;
		try
		{
			text = StrictUtf8.GetString(data, offset, data.Length - offset);
		}
		catch (DecoderFallbackException)
		{
			return false;
		}
		string trimmed = text.Trim().TrimStart('\uFEFF').Trim();
		if (trimmed.Length == 0 ||
			trimmed.Contains('<', StringComparison.Ordinal) ||
			trimmed.Contains('>', StringComparison.Ordinal) ||
			!trimmed.StartsWith("M", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		barePath = trimmed;
		return true;
	}

	private static void ValidateAssetReferences(
		List<string> referencedKeys,
		IReadOnlyList<AssetPayload> assets)
	{
		var assetKeys = new HashSet<string>(
			assets.Select(asset => asset.Key),
			StringComparer.OrdinalIgnoreCase);
		foreach (string key in referencedKeys)
		{
			if (!assetKeys.Contains(key))
			{
				throw Fail(
					PortableConfigBundleReasonCodes.AssetMissing,
					"The configuration references an asset that is not present in the bundle.");
			}
		}
		var referenced = new HashSet<string>(referencedKeys, StringComparer.OrdinalIgnoreCase);
		if (assets.Any(asset => !referenced.Contains(asset.Key)))
		{
			throw Fail(
				PortableConfigBundleReasonCodes.AssetUnused,
				"The bundle contains an asset that is not referenced by the configuration.");
		}
	}

	private static byte[] BuildManifest(IReadOnlyList<AssetSource> assets)
	{
		var manifestAssets = new JsonArray();
		foreach (AssetSource asset in assets)
		{
			string entry = "icons/" + Convert.ToHexString(
				SHA256.HashData(Concat(
					Utf8WithoutBom.GetBytes(asset.Key.ToUpperInvariant()),
					asset.Data))).ToLowerInvariant() + ".svg";
			asset.EntryName = entry;
			manifestAssets.Add(new JsonObject
			{
				["key"] = asset.Key,
				["entry"] = entry,
				["size"] = asset.Data.LongLength,
				["sha256"] = Convert.ToHexString(SHA256.HashData(asset.Data)).ToLowerInvariant()
			});
		}

		var manifest = new JsonObject
		{
			["format"] = Format,
			["version"] = Version,
			["config"] = ConfigEntryName,
			["assets"] = manifestAssets
		};
		byte[] bytes = Utf8WithoutBom.GetBytes(manifest.ToJsonString(CompactJsonOptions));
		if (bytes.LongLength > MaxManifestBytes)
		{
			throw Fail(PortableConfigBundleReasonCodes.ManifestTooLarge, "The generated manifest is too large.");
		}
		return bytes;
	}

	private static PackageContents ReadPackage(ZipArchive archive)
	{
		var entries = archive.Entries.ToList();
		if (entries.Count == 0)
		{
			throw Fail(PortableConfigBundleReasonCodes.ZipInvalid, "The bundle is empty.");
		}

		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (ZipArchiveEntry entry in entries)
		{
			ValidateEntryName(entry.FullName);
			if (!names.Add(entry.FullName))
			{
				throw Fail(PortableConfigBundleReasonCodes.DuplicateEntry, "The bundle contains duplicate ZIP entries.");
			}
		}

		ZipArchiveEntry manifestEntry = RequireEntry(entries, ManifestEntryName);
		byte[] manifestBytes = ReadEntryBounded(manifestEntry, MaxManifestBytes, PortableConfigBundleReasonCodes.ManifestTooLarge);
		ValidateNoDuplicateManifestProperties(manifestBytes);
		JsonDocument manifest;
		try
		{
			manifest = JsonDocument.Parse(
				manifestBytes,
				new JsonDocumentOptions
				{
					AllowTrailingCommas = false,
					CommentHandling = JsonCommentHandling.Disallow,
					MaxDepth = 64
				});
		}
		catch (JsonException ex)
		{
			throw Fail(PortableConfigBundleReasonCodes.ManifestInvalid, "The bundle manifest is not valid JSON.", ex);
		}

		using (manifest)
		{
			JsonElement root = manifest.RootElement;
			if (root.ValueKind != JsonValueKind.Object ||
				!root.TryGetProperty("format", out JsonElement formatElement) ||
				formatElement.ValueKind != JsonValueKind.String ||
				!string.Equals(formatElement.GetString(), Format, StringComparison.Ordinal))
			{
				throw Fail(PortableConfigBundleReasonCodes.UnsupportedFormat, "The bundle format is not supported.");
			}
			if (!root.TryGetProperty("version", out JsonElement versionElement) ||
				versionElement.ValueKind != JsonValueKind.Number ||
				!versionElement.TryGetInt32(out int version) ||
				version != Version)
			{
				throw Fail(PortableConfigBundleReasonCodes.UnsupportedVersion, "The bundle version is not supported.");
			}
			if (!root.TryGetProperty("config", out JsonElement configElement) ||
				configElement.ValueKind != JsonValueKind.String ||
				!string.Equals(configElement.GetString(), ConfigEntryName, StringComparison.Ordinal))
			{
				throw Fail(PortableConfigBundleReasonCodes.ManifestInvalid, "The bundle config entry is invalid.");
			}
			if (!root.TryGetProperty("assets", out JsonElement assetsElement) ||
				assetsElement.ValueKind != JsonValueKind.Array)
			{
				throw Fail(PortableConfigBundleReasonCodes.ManifestInvalid, "The bundle asset list is missing.");
			}

			var assetPayloads = new List<AssetPayload>();
			var assetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var assetEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			long totalSize = 0;
			foreach (JsonElement assetElement in assetsElement.EnumerateArray())
			{
				if (assetPayloads.Count >= MaxAssetCount)
				{
					throw Fail(PortableConfigBundleReasonCodes.AssetCountLimit, "The bundle contains too many assets.");
				}
				if (assetElement.ValueKind != JsonValueKind.Object ||
					!assetElement.TryGetProperty("key", out JsonElement keyElement) ||
					keyElement.ValueKind != JsonValueKind.String ||
					!assetElement.TryGetProperty("entry", out JsonElement entryElement) ||
					entryElement.ValueKind != JsonValueKind.String ||
					!assetElement.TryGetProperty("size", out JsonElement sizeElement) ||
					sizeElement.ValueKind != JsonValueKind.Number ||
					!sizeElement.TryGetInt64(out long size) ||
					!assetElement.TryGetProperty("sha256", out JsonElement hashElement) ||
					hashElement.ValueKind != JsonValueKind.String)
				{
					throw Fail(PortableConfigBundleReasonCodes.ManifestInvalid, "An asset record is malformed.");
				}

				string key = keyElement.GetString() ?? "";
				string entry = entryElement.GetString() ?? "";
				string hash = hashElement.GetString() ?? "";
				if (!key.StartsWith(CustomPrefix, StringComparison.OrdinalIgnoreCase) ||
					key.Length == CustomPrefix.Length ||
					key.IndexOfAny(new[] { '/', '\\', '\0' }) >= 0)
				{
					throw Fail(PortableConfigBundleReasonCodes.ManifestInvalid, "An asset key is invalid.");
				}
				if (!IsSafeAssetEntry(entry))
				{
					throw Fail(PortableConfigBundleReasonCodes.EntryInvalid, "An asset entry name is invalid.");
				}
				if (!assetKeys.Add(key) || !assetEntries.Add(entry))
				{
					throw Fail(PortableConfigBundleReasonCodes.DuplicateEntry, "The manifest contains duplicate assets.");
				}
				if (size < 0 || size > MaxAssetBytes)
				{
					throw Fail(PortableConfigBundleReasonCodes.AssetSizeLimit, "An asset size is outside the allowed range.");
				}
				if (totalSize > MaxTotalAssetBytes - size)
				{
					throw Fail(PortableConfigBundleReasonCodes.ResourceSizeLimit, "The assets exceed the total size limit.");
				}
				totalSize += size;
				if (hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)))
				{
					throw Fail(PortableConfigBundleReasonCodes.ManifestInvalid, "An asset hash is invalid.");
				}

				ZipArchiveEntry assetEntry = RequireEntry(entries, entry);
				byte[] data = ReadEntryBounded(assetEntry, MaxAssetBytes, PortableConfigBundleReasonCodes.AssetSizeLimit);
				if (data.LongLength != size)
				{
					throw Fail(PortableConfigBundleReasonCodes.SizeMismatch, "An asset size does not match the manifest.");
				}
				string actualHash = Convert.ToHexString(SHA256.HashData(data));
				if (!string.Equals(actualHash, hash, StringComparison.OrdinalIgnoreCase))
				{
					throw Fail(PortableConfigBundleReasonCodes.HashMismatch, "An asset hash does not match the manifest.");
				}
				assetPayloads.Add(new AssetPayload(key, entry, data));
			}
			ZipArchiveEntry configEntry = RequireEntry(entries, ConfigEntryName);
			byte[] configBytes = ReadEntryBounded(configEntry, MaxConfigBytes, PortableConfigBundleReasonCodes.ConfigTooLarge);
			string configJson;
			try
			{
				configJson = StrictUtf8.GetString(configBytes);
			}
			catch (DecoderFallbackException ex)
			{
				throw Fail(PortableConfigBundleReasonCodes.ConfigInvalid, "The configuration is not valid UTF-8.", ex);
			}
			JsonObject configRoot = ParseConfigObject(configJson);

			var allowedEntries = new HashSet<string>(StringComparer.Ordinal)
			{
				ManifestEntryName,
				ConfigEntryName
			};
			foreach (AssetPayload asset in assetPayloads)
			{
				allowedEntries.Add(asset.EntryName);
			}
			if (entries.Any(entry => !allowedEntries.Contains(entry.FullName)))
			{
				throw Fail(PortableConfigBundleReasonCodes.ExtraEntry, "The bundle contains an extra ZIP entry.");
			}
			if (entries.Count != allowedEntries.Count)
			{
				throw Fail(PortableConfigBundleReasonCodes.MissingEntry, "The bundle is missing a declared ZIP entry.");
			}

			return new PackageContents(configJson, configRoot, assetPayloads);
		}
	}

	private static void ValidateNoDuplicateManifestProperties(byte[] bytes)
	{
		try
		{
			var reader = new Utf8JsonReader(
				bytes,
				new JsonReaderOptions
				{
					AllowTrailingCommas = false,
					CommentHandling = JsonCommentHandling.Disallow,
					MaxDepth = 64
				});
			var objectProperties = new Stack<HashSet<string>>();
			while (reader.Read())
			{
				if (reader.TokenType == JsonTokenType.StartObject)
				{
					objectProperties.Push(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
				}
				else if (reader.TokenType == JsonTokenType.EndObject)
				{
					if (objectProperties.Count == 0)
					{
						throw Fail(PortableConfigBundleReasonCodes.ManifestInvalid, "The manifest object structure is invalid.");
					}
					objectProperties.Pop();
				}
				else if (reader.TokenType == JsonTokenType.PropertyName)
				{
					if (objectProperties.Count == 0)
					{
						throw Fail(PortableConfigBundleReasonCodes.ManifestInvalid, "The manifest property structure is invalid.");
					}
					string propertyName = reader.GetString() ?? "";
					if (!objectProperties.Peek().Add(propertyName))
					{
						throw Fail(PortableConfigBundleReasonCodes.ManifestInvalid, "The manifest contains duplicate properties.");
					}
				}
			}
			if (objectProperties.Count != 0)
			{
				throw Fail(PortableConfigBundleReasonCodes.ManifestInvalid, "The manifest is incomplete.");
			}
		}
		catch (PortableConfigBundleException)
		{
			throw;
		}
		catch (JsonException ex)
		{
			throw Fail(PortableConfigBundleReasonCodes.ManifestInvalid, "The manifest is not valid JSON.", ex);
		}
	}

	private static void ValidateEntryName(string name)
	{
		if (string.IsNullOrWhiteSpace(name) ||
			name.Contains('\\') ||
			name.StartsWith("/", StringComparison.Ordinal) ||
			name.EndsWith("/", StringComparison.Ordinal) ||
			name.Contains("//", StringComparison.Ordinal) ||
			name.IndexOfAny(new[] { '\0', ':', '*' , '?', '"', '<', '>', '|' }) >= 0)
		{
			throw Fail(PortableConfigBundleReasonCodes.EntryInvalid, "A ZIP entry name is invalid.");
		}
		string[] segments = name.Split('/');
		if (segments.Any(segment => segment.Length == 0 || segment == "." || segment == ".."))
		{
			throw Fail(PortableConfigBundleReasonCodes.EntryInvalid, "A ZIP entry path is invalid.");
		}
	}

	private static bool IsSafeAssetEntry(string name)
	{
		if (!name.StartsWith("icons/", StringComparison.Ordinal) || name.Length <= "icons/".Length + 4)
		{
			return false;
		}
		string fileName = name.Substring("icons/".Length);
		if (!fileName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
			fileName.Length > 128 ||
			fileName[0] == '.')
		{
			return false;
		}
		foreach (char character in fileName)
		{
			if (!(char.IsAsciiLetterOrDigit(character) || character == '.' || character == '_' || character == '-'))
			{
				return false;
			}
		}
		return true;
	}

	private static ZipArchiveEntry RequireEntry(IReadOnlyList<ZipArchiveEntry> entries, string name)
	{
		ZipArchiveEntry? entry = entries.FirstOrDefault(item =>
			string.Equals(item.FullName, name, StringComparison.Ordinal));
		if (entry == null)
		{
			throw Fail(PortableConfigBundleReasonCodes.MissingEntry, "The bundle is missing an expected ZIP entry.");
		}
		return entry;
	}

	private static byte[] ReadEntryBounded(ZipArchiveEntry entry, long maxBytes, string tooLargeReason)
	{
		try
		{
			if (entry.Length > maxBytes)
			{
				throw Fail(tooLargeReason, "A ZIP entry exceeds its size limit.");
			}
			using Stream stream = entry.Open();
			using var memory = new MemoryStream();
			byte[] buffer = new byte[64 * 1024];
			long total = 0;
			while (true)
			{
				int read = stream.Read(buffer, 0, buffer.Length);
				if (read == 0)
				{
					break;
				}
				total += read;
				if (total > maxBytes)
				{
					throw Fail(tooLargeReason, "A ZIP entry exceeds its size limit.");
				}
				memory.Write(buffer, 0, read);
			}
			return memory.ToArray();
		}
		catch (PortableConfigBundleException)
		{
			throw;
		}
		catch (Exception ex)
		{
			throw Fail(PortableConfigBundleReasonCodes.ZipInvalid, "Unable to read a ZIP entry.", ex);
		}
	}

	private static void WriteZipEntry(ZipArchive archive, string name, byte[] data)
	{
		ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
		using Stream stream = entry.Open();
		stream.Write(data, 0, data.Length);
	}

	private static byte[] ReadFileBounded(string path, long maxBytes, string tooLargeReason)
	{
		try
		{
			using var file = new FileStream(
				path,
				FileMode.Open,
				FileAccess.Read,
				FileShare.Read,
				64 * 1024,
				FileOptions.SequentialScan);
			if (file.Length > maxBytes)
			{
				throw Fail(tooLargeReason, "A custom icon exceeds its size limit.");
			}
			using var memory = new MemoryStream();
			byte[] buffer = new byte[64 * 1024];
			long total = 0;
			while (true)
			{
				int read = file.Read(buffer, 0, buffer.Length);
				if (read == 0)
				{
					break;
				}
				total += read;
				if (total > maxBytes)
				{
					throw Fail(tooLargeReason, "A custom icon exceeds its size limit.");
				}
				memory.Write(buffer, 0, read);
			}
			return memory.ToArray();
		}
		catch (PortableConfigBundleException)
		{
			throw;
		}
		catch (Exception ex)
		{
			throw Fail(PortableConfigBundleReasonCodes.IoError, "Unable to read a custom icon.", ex);
		}
	}

	private static string CreateUniqueAsset(string directory, string originalKey, byte[] data, List<string> createdFiles)
	{
		string originalStem = originalKey.StartsWith(CustomPrefix, StringComparison.OrdinalIgnoreCase)
			? originalKey.Substring(CustomPrefix.Length)
			: "icon";
		string baseStem = SanitizeStem(originalStem);
		string keyHash = Convert.ToHexString(
			SHA256.HashData(Utf8WithoutBom.GetBytes(originalKey.ToUpperInvariant()))).ToLowerInvariant();
		for (int attempt = 0; attempt < 10; attempt++)
		{
			string stem = baseStem + "_portable_" + keyHash.Substring(0, 8) + "_" + Guid.NewGuid().ToString("N");
			string path = Path.Combine(directory, stem + ".svg");
			bool created = false;
			try
			{
				using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.SequentialScan))
				{
					created = true;
					file.Write(data, 0, data.Length);
					file.Flush(true);
				}
				createdFiles.Add(path);
				return CustomPrefix + stem;
			}
			catch (IOException) when (File.Exists(path))
			{
				if (created)
				{
					TryDeleteFile(path);
				}
				// Retry with a new unique stem without touching the existing icon.
			}
			catch
			{
				if (created)
				{
					TryDeleteFile(path);
				}
				throw Fail(PortableConfigBundleReasonCodes.IoError, "Unable to create a unique custom icon.");
			}
		}
		throw Fail(PortableConfigBundleReasonCodes.IoError, "Unable to allocate a unique custom icon name.");
	}

	private static string SanitizeStem(string value)
	{
		string result = value.Trim();
		foreach (char invalid in Path.GetInvalidFileNameChars())
		{
			result = result.Replace(invalid, '_');
		}
		result = result.Trim().TrimEnd('.', ' ');
		if (result.Length == 0)
		{
			result = "icon";
		}
		return result.Length <= 48 ? result : result[..48];
	}

	private static byte[] EncodeUtf8(string value, string reasonCode)
	{
		try
		{
			return StrictUtf8.GetBytes(value);
		}
		catch (EncoderFallbackException ex)
		{
			throw Fail(reasonCode, "The configuration contains invalid text.", ex);
		}
	}

	private static byte[] Concat(byte[] first, byte[] second)
	{
		var result = new byte[first.Length + second.Length];
		Buffer.BlockCopy(first, 0, result, 0, first.Length);
		Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
		return result;
	}

	private static PortableConfigBundleException Fail(string reasonCode, string message, Exception? inner = null)
		=> new(reasonCode, message, inner);

	private static void RollbackFiles(IReadOnlyList<string> paths)
	{
		for (int index = paths.Count - 1; index >= 0; index--)
		{
			TryDeleteFile(paths[index]);
		}
	}

	private static void TryDeleteFile(string path)
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

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			string fullPath = Path.GetFullPath(path);
			string tempPath = Path.GetFullPath(Path.GetTempPath());
			if (!fullPath.StartsWith(
				tempPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
				StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			if (Directory.Exists(fullPath))
			{
				Directory.Delete(fullPath, recursive: true);
			}
		}
		catch
		{
		}
	}

	private static void TryDeleteEmptyDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
			{
				Directory.Delete(path);
			}
		}
		catch
		{
		}
	}

	private sealed class AssetSource
	{
		public AssetSource(string key, string entryName, byte[] data)
		{
			Key = key;
			EntryName = entryName;
			Data = data;
		}

		public string Key { get; }
		public string EntryName { get; set; }
		public byte[] Data { get; }
	}

	private sealed class AssetPayload
	{
		public AssetPayload(string key, string entryName, byte[] data)
		{
			Key = key;
			EntryName = entryName;
			Data = data;
		}

		public string Key { get; }
		public string EntryName { get; }
		public byte[] Data { get; }
	}

	private sealed class PackageContents
	{
		public PackageContents(string configJson, JsonObject configRoot, List<AssetPayload> assets)
		{
			ConfigJson = configJson;
			ConfigRoot = configRoot;
			Assets = assets;
		}

		public string ConfigJson { get; }
		public JsonObject ConfigRoot { get; }
		public List<AssetPayload> Assets { get; }
	}
}
