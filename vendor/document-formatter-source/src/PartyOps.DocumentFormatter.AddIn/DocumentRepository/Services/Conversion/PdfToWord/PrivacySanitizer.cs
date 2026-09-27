using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public static class PrivacySanitizer
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string ShortHash(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "00000000";
		}
		using SHA256 sHA = SHA256.Create();
		byte[] array = sHA.ComputeHash(Encoding.UTF8.GetBytes(text));
		StringBuilder stringBuilder = new StringBuilder(8);
		for (int i = 0; i < 4; i++)
		{
			stringBuilder.Append(array[i].ToString("x2"));
		}
		return stringBuilder.ToString();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string LengthAndHash(string text)
	{
		return "len=" + ((!string.IsNullOrEmpty(text)) ? text.Length : 0) + " hash=" + ShortHash(text);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string PathFingerprint(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return "none";
		}
		string text = Path.GetExtension(path);
		if (string.IsNullOrEmpty(text))
		{
			text = "(noext)";
		}
		return text + "#" + ShortHash(path);
	}

	public static string ScrubPaths(string message, params string[] paths)
	{
		if (!string.IsNullOrEmpty(message) && paths != null)
		{
			string text = message;
			foreach (string text2 in paths)
			{
				if (!string.IsNullOrEmpty(text2))
				{
					text = text.Replace(text2, PathFingerprint(text2));
				}
			}
			return text;
		}
		return message;
	}
}
