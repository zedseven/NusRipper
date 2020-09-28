using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NusRipper
{
	public static class Helpers
	{
		public static T[] Slice<T>(this IReadOnlyList<T> arr, int start, int length, bool normalizeEndianness = true)
		{
			T[] ret = new T[length];
			for (int i = 0; i < length; i++)
				ret[i] = arr[i + start];
			if (normalizeEndianness && BitConverter.IsLittleEndian)
				Array.Reverse(ret);
			return ret;
		}

		public static byte[] HexStringToBytes(string hexString)
		{
			byte[] ret = new byte[hexString.Length / 2];
			for(int i = 0; i < hexString.Length / 2; i++)
				ret[i] = Convert.ToByte(hexString.Substring(i * 2, 2), 16);
			return ret;
		}

		public static T[] Pad<T>(this T[] arr, int length, T value = default)
			=> arr.Concat(Enumerable.Repeat(value, length - arr.Length)).ToArray();

		// https://stackoverflow.com/a/12800424/6003488
		public static string GetSafeFilename(string filename)
			=> string.Join("", filename.Split(Path.GetInvalidFileNameChars()));
	}
}
