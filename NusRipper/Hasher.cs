using System.IO;
using System.Security.Cryptography;
using System.Text;
using NullFX.CRC;

namespace NusRipper
{
	public static class Hasher
	{
		public class FileHashCollection
		{
			// File Info
			public readonly long FileSize;

			// Hashes
			public readonly uint Crc32Hash;
			public readonly byte[] Md5Hash;
			public readonly byte[] Sha1Hash;
			public readonly byte[] Sha256Hash;

			// Hash String Representations
			public string Crc32String => Crc32Hash.ToString("x8");
			public string Md5String => Md5Hash.ToHexString();
			public string Sha1String => Sha1Hash.ToHexString();
			public string Sha256String => Sha256Hash.ToHexString();

			public FileHashCollection(string filePath)
			{
				byte[] bytes = File.ReadAllBytes(filePath);

				FileSize = bytes.LongLength;
				Crc32Hash = Crc32.ComputeChecksum(bytes);
				Md5Hash = MD5.Create().ComputeHash(bytes);
				Sha1Hash = SHA1.Create().ComputeHash(bytes);
				Sha256Hash = SHA256.Create().ComputeHash(bytes);
			}

			public void WriteToFile(string filePath)
			{
				using (StreamWriter sw = File.CreateText(filePath))
				{
					sw.WriteLine($"Size: {FileSize}");
					sw.WriteLine($"CRC32: {Crc32String}");
					sw.WriteLine($"MD5: {Md5String}");
					sw.WriteLine($"SHA1: {Sha1String}");
					sw.WriteLine($"SHA256: {Sha256String}");
				}
			}
		}

		private static string ToHexString(this byte[] bytes)
		{
			StringBuilder hex = new StringBuilder(bytes.Length * 2);
			foreach (byte b in bytes)
				hex.AppendFormat("{0:x2}", b);
			return hex.ToString();
		}
	}
}
