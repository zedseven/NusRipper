using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace NusRipper
{
	public static class Helpers
	{
		[Pure]
		public static T[] Slice<T>(this IReadOnlyList<T> arr, int start, int length, bool normalizeEndianness = false)
		{
			T[] ret = new T[length];
			for (int i = 0; i < length; i++)
				ret[i] = arr[i + start];
			if (normalizeEndianness && BitConverter.IsLittleEndian)
				Array.Reverse(ret);
			return ret;
		}

		[Pure]
		public static byte[] HexStringToBytes(string hexString)
		{
			byte[] ret = new byte[hexString.Length / 2];
			for(int i = 0; i < hexString.Length / 2; i++)
				ret[i] = Convert.ToByte(hexString.Substring(i * 2, 2), 16);
			return ret;
		}

		[Pure]
		public static string ToHexString(this byte[] bytes)
		{
			StringBuilder hex = new StringBuilder(bytes.Length * 2);
			foreach (byte b in bytes)
				hex.AppendFormat("{0:x2}", b);
			return hex.ToString();
		}

		[Pure]
		public static T[] Pad<T>(this T[] arr, int length, T value = default)
			=> arr.Concat(Enumerable.Repeat(value, length - arr.Length)).ToArray();

		[Pure]
		// https://stackoverflow.com/a/12800424/6003488
		public static string GetSafeFilename(string filename)
			=> string.Join("", filename.Split(Path.GetInvalidFileNameChars()));

		[Pure]
		public static string AsNullIfEmpty(this string str)
			=> str.Length <= 0 ? null : str;

		[Pure]
		public static string ToNiceString(this TimeSpan timeSpan)
		{
			if (timeSpan.TotalHours >= 24)
				return $"{timeSpan.Days}d {timeSpan.Hours}h {timeSpan.Minutes}m {timeSpan.Seconds}s";
			if (timeSpan.TotalMinutes >= 60)
				return $"{timeSpan.Hours}h {timeSpan.Minutes}m {timeSpan.Seconds}s";
			if (timeSpan.TotalSeconds >= 60)
				return $"{timeSpan.Minutes}m {timeSpan.Seconds}s";
			return $"{timeSpan.Seconds}s";
		}

		[Pure]
		public static char[] AsChars(this byte[] bytes)
			=> bytes.Select(b => (char) b).ToArray();

		// XML Stuff
		public static async Task WriteAttributeAsync(this XmlWriter writer, string name, string value)
		{
			writer.WriteStartAttribute(name);
			await writer.WriteStringAsync(value ?? "");
			writer.WriteEndAttribute();
		}
	}
}
