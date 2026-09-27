using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace DocumentRepository.Services.Configuration;

internal static class ConfigurationPackageCodec
{
	internal const string ManifestPath = "manifest.json";

	private const long MaxPackageBytes = 10485760L;

	private const long MaxManifestBytes = 131072L;

	private const long MaxEntryBytes = 5242880L;

	private const long MaxTotalExpandedBytes = 10485760L;

	private const int MaxEntryCount = 16;

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static ConfigurationPackagePayload Create(string productId, string productName, string productVersion, int schemaVersion, DateTime exportedAt, IEnumerable<KeyValuePair<string, KeyValuePair<string, byte[]>>> entries)
	{
		if (!string.IsNullOrWhiteSpace(productId))
		{
			if (schemaVersion > 0)
			{
				if (entries != null)
				{
					ConfigurationPackageManifest configurationPackageManifest = new ConfigurationPackageManifest
					{
						ProductId = productId.Trim(),
						ProductName = (productName ?? string.Empty).Trim(),
						ProductVersion = (productVersion ?? string.Empty).Trim(),
						PackageSchemaVersion = schemaVersion,
						ExportedAt = exportedAt.ToUniversalTime().ToString("O")
					};
					Dictionary<string, byte[]> dictionary = new Dictionary<string, byte[]>(StringComparer.Ordinal);
					HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					foreach (KeyValuePair<string, KeyValuePair<string, byte[]>> entry in entries)
					{
						string text = (entry.Key ?? string.Empty).Trim();
						string text2 = (entry.Value.Key ?? string.Empty).Trim();
						byte[] value = entry.Value.Value;
						ValidateEntryIdentity(text, text2);
						if (!hashSet.Add(text))
						{
							throw new InvalidDataException("参数包包含重复的配置编号：" + text);
						}
						if (!dictionary.ContainsKey(text2))
						{
							if (value != null && value.Length != 0)
							{
								if (value.LongLength > 5242880)
								{
									throw new InvalidDataException("参数内容超过允许大小：" + text);
								}
								byte[] array = (byte[])value.Clone();
								dictionary.Add(text2, array);
								configurationPackageManifest.Entries.Add(new ConfigurationPackageEntry
								{
									Id = text,
									Path = text2,
									Length = array.LongLength,
									Sha256 = ComputeSha256(array)
								});
								continue;
							}
							throw new InvalidDataException("参数内容为空：" + text);
						}
						throw new InvalidDataException("参数包包含重复的配置路径：" + text2);
					}
					if (configurationPackageManifest.Entries.Count == 0 || configurationPackageManifest.Entries.Count > 16)
					{
						throw new InvalidDataException("参数包配置数量无效。");
					}
					return new ConfigurationPackagePayload
					{
						Manifest = configurationPackageManifest,
						Contents = dictionary
					};
				}
				throw new ArgumentNullException("entries");
			}
			throw new ArgumentOutOfRangeException("schemaVersion");
		}
		throw new ArgumentException("参数包产品编号不能为空。", "productId");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static void Write(string path, ConfigurationPackagePayload payload)
	{
		if (!string.IsNullOrWhiteSpace(path))
		{
			ValidatePayload(payload);
			JavaScriptSerializer val = new JavaScriptSerializer
			{
				MaxJsonLength = 131072
			};
			byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(val.Serialize((object)payload.Manifest));
			if (bytes.LongLength > 131072)
			{
				throw new InvalidDataException("参数包清单超过允许大小。");
			}
			using FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
			using ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8);
			WriteEntry(archive, "manifest.json", bytes);
			foreach (ConfigurationPackageEntry entry in payload.Manifest.Entries)
			{
				WriteEntry(archive, entry.Path, payload.Contents[entry.Path]);
			}
			return;
		}
		throw new ArgumentException("参数包保存路径不能为空。", "path");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static ConfigurationPackagePayload Read(string path)
	{
		if (!string.IsNullOrWhiteSpace(path))
		{
			if (File.Exists(path))
			{
				FileInfo fileInfo = new FileInfo(path);
				if (fileInfo.Length <= 0 || fileInfo.Length > 10485760)
				{
					throw new InvalidDataException("参数包大小无效或超过 10 MB 限制。");
				}
				using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
				using ZipArchive zipArchive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false, Encoding.UTF8);
				if (zipArchive.Entries.Count != 0 && zipArchive.Entries.Count <= 17)
				{
					Dictionary<string, ZipArchiveEntry> dictionary = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
					long num = 0L;
					foreach (ZipArchiveEntry entry in zipArchive.Entries)
					{
						ValidateArchivePath(entry.FullName);
						if (!dictionary.ContainsKey(entry.FullName))
						{
							dictionary.Add(entry.FullName, entry);
							if (entry.Length >= 0 && entry.Length <= 5242880)
							{
								num += entry.Length;
								if (num > 10485760)
								{
									throw new InvalidDataException("参数包解压后超过 10 MB 限制。");
								}
								continue;
							}
							throw new InvalidDataException("参数包内文件超过允许大小：" + entry.FullName);
						}
						throw new InvalidDataException("参数包包含重复文件：" + entry.FullName);
					}
					if (dictionary.TryGetValue("manifest.json", out var value))
					{
						if (value.Length <= 0 || value.Length > 131072)
						{
							throw new InvalidDataException("参数包清单大小无效。");
						}
						JavaScriptSerializer val = new JavaScriptSerializer
						{
							MaxJsonLength = 131072
						};
						ConfigurationPackageManifest configurationPackageManifest;
						try
						{
							configurationPackageManifest = val.Deserialize<ConfigurationPackageManifest>(Encoding.UTF8.GetString(ReadEntry(value, 131072L)));
						}
						catch (Exception innerException)
						{
							throw new InvalidDataException("参数包清单无法读取。", innerException);
						}
						if (configurationPackageManifest != null && configurationPackageManifest.Entries != null && configurationPackageManifest.Entries.Count != 0 && configurationPackageManifest.Entries.Count <= 16)
						{
							if (dictionary.Count != configurationPackageManifest.Entries.Count + 1)
							{
								throw new InvalidDataException("参数包包含未在清单登记的文件。");
							}
							Dictionary<string, byte[]> dictionary2 = new Dictionary<string, byte[]>(StringComparer.Ordinal);
							HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
							foreach (ConfigurationPackageEntry entry2 in configurationPackageManifest.Entries)
							{
								if (entry2 != null)
								{
									ValidateEntryIdentity(entry2.Id, entry2.Path);
									if (hashSet.Add(entry2.Id))
									{
										if (!dictionary2.ContainsKey(entry2.Path))
										{
											if (!dictionary.TryGetValue(entry2.Path, out var value2))
											{
												throw new InvalidDataException("参数包缺少配置文件：" + entry2.Path);
											}
											byte[] array = ReadEntry(value2, 5242880L);
											if (entry2.Length != array.LongLength)
											{
												throw new InvalidDataException("参数文件大小校验失败：" + entry2.Path);
											}
											if (!string.Equals(entry2.Sha256, ComputeSha256(array), StringComparison.OrdinalIgnoreCase))
											{
												throw new InvalidDataException("参数文件完整性校验失败：" + entry2.Path);
											}
											dictionary2.Add(entry2.Path, array);
											continue;
										}
										throw new InvalidDataException("参数包包含重复的配置路径：" + entry2.Path);
									}
									throw new InvalidDataException("参数包包含重复的配置编号：" + entry2.Id);
								}
								throw new InvalidDataException("参数包清单包含空配置项。");
							}
							return new ConfigurationPackagePayload
							{
								Manifest = configurationPackageManifest,
								Contents = dictionary2
							};
						}
						throw new InvalidDataException("参数包清单内容无效。");
					}
					throw new InvalidDataException("参数包缺少 manifest.json 清单。");
				}
				throw new InvalidDataException("参数包文件数量无效。");
			}
			throw new FileNotFoundException("参数包不存在。", path);
		}
		throw new ArgumentException("参数包路径不能为空。", "path");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static string ComputeSha256(byte[] bytes)
	{
		if (bytes == null)
		{
			throw new ArgumentNullException("bytes");
		}
		using SHA256 sHA = SHA256.Create();
		byte[] array = sHA.ComputeHash(bytes);
		StringBuilder stringBuilder = new StringBuilder(array.Length * 2);
		byte[] array2 = array;
		foreach (byte b in array2)
		{
			stringBuilder.Append(b.ToString("x2"));
		}
		return stringBuilder.ToString();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidatePayload(ConfigurationPackagePayload payload)
	{
		if (payload != null && payload.Manifest != null && payload.Contents != null)
		{
			if (payload.Manifest.Entries == null || payload.Manifest.Entries.Count == 0)
			{
				throw new InvalidDataException("参数包没有可写入的配置。");
			}
			{
				foreach (ConfigurationPackageEntry entry in payload.Manifest.Entries)
				{
					ValidateEntryIdentity(entry.Id, entry.Path);
					if (!payload.Contents.TryGetValue(entry.Path, out var value) || value == null || value.Length == 0)
					{
						throw new InvalidDataException("参数包缺少配置内容：" + entry.Path);
					}
					if (entry.Length == value.LongLength && string.Equals(entry.Sha256, ComputeSha256(value), StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					throw new InvalidDataException("参数包配置清单与内容不一致：" + entry.Path);
				}
				return;
			}
		}
		throw new InvalidDataException("参数包内容不能为空。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateEntryIdentity(string id, string path)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			throw new InvalidDataException("参数包配置编号不能为空。");
		}
		ValidateArchivePath(path);
		if (!path.StartsWith("data/", StringComparison.Ordinal) || path.EndsWith("/", StringComparison.Ordinal))
		{
			throw new InvalidDataException("参数包配置路径无效：" + path);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateArchivePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.IndexOf('\\') >= 0 || path.Contains("../") || path.Contains("/..") || path.Contains(":"))
		{
			throw new InvalidDataException("参数包包含不安全的文件路径：" + path);
		}
	}

	private static void WriteEntry(ZipArchive archive, string path, byte[] bytes)
	{
		ZipArchiveEntry zipArchiveEntry = archive.CreateEntry(path, CompressionLevel.Optimal);
		zipArchiveEntry.LastWriteTime = DateTimeOffset.Now;
		using Stream stream = zipArchiveEntry.Open();
		stream.Write(bytes, 0, bytes.Length);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static byte[] ReadEntry(ZipArchiveEntry entry, long maxBytes)
	{
		if (entry.Length < 0 || entry.Length > maxBytes)
		{
			throw new InvalidDataException("参数包内文件超过允许大小：" + entry.FullName);
		}
		using Stream stream = entry.Open();
		using MemoryStream memoryStream = new MemoryStream((int)entry.Length);
		byte[] array = new byte[81920];
		long num = 0L;
		int num2;
		while ((num2 = stream.Read(array, 0, array.Length)) > 0)
		{
			num += num2;
			if (num > maxBytes)
			{
				throw new InvalidDataException("参数包内文件超过允许大小：" + entry.FullName);
			}
			memoryStream.Write(array, 0, num2);
		}
		return memoryStream.ToArray();
	}
}
