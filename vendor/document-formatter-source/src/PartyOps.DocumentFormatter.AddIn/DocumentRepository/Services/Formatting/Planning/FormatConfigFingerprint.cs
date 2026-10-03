using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace DocumentRepository.Services.Formatting.Planning;

internal static class FormatConfigFingerprint
{
	public static string Build(object value)
	{
		StringBuilder stringBuilder = new StringBuilder(4096);
		AppendValue(stringBuilder, value, 0);
		return Sha256(stringBuilder.ToString());
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AppendValue(StringBuilder builder, object value, int depth)
	{
		if (value != null)
		{
			if (depth > 5)
			{
				builder.Append("<max-depth>");
				return;
			}
			Type type = value.GetType();
			if (IsScalar(type))
			{
				builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
				return;
			}
			if (value is IEnumerable enumerable && !(value is string))
			{
				builder.Append('[');
				foreach (object item in enumerable)
				{
					AppendValue(builder, item, depth + 1);
					builder.Append(';');
				}
				builder.Append(']');
				return;
			}
			builder.Append(type.FullName).Append('{');
			PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
			Array.Sort(properties, (PropertyInfo a, PropertyInfo b) => string.CompareOrdinal(a.Name, b.Name));
			PropertyInfo[] array = properties;
			foreach (PropertyInfo propertyInfo in array)
			{
				if (propertyInfo.GetIndexParameters().Length == 0 && propertyInfo.CanRead)
				{
					builder.Append(propertyInfo.Name).Append('=');
					try
					{
						AppendValue(builder, propertyInfo.GetValue(value, null), depth + 1);
					}
					catch
					{
						builder.Append("<unreadable>");
					}
					builder.Append(';');
				}
			}
			builder.Append('}');
		}
		else
		{
			builder.Append("<null>");
		}
	}

	private static bool IsScalar(Type type)
	{
		if (!type.IsPrimitive && !type.IsEnum && !(type == typeof(string)) && !(type == typeof(decimal)) && !(type == typeof(DateTime)))
		{
			return type == typeof(TimeSpan);
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string Sha256(string text)
	{
		using SHA256 sHA = SHA256.Create();
		byte[] array = sHA.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
		StringBuilder stringBuilder = new StringBuilder(array.Length * 2);
		byte[] array2 = array;
		foreach (byte b in array2)
		{
			stringBuilder.Append(b.ToString("X2"));
		}
		return stringBuilder.ToString();
	}
}
