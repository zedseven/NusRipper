using System.IO;
using System.Security.Cryptography;
using NullFX.CRC;

namespace NusRipper
{
	public static class Hasher
	{
		public class FileHashCollection
		{
			private const string SizeLabel = "Size";
			private const string Crc32Label = "CRC32";
			private const string Md5Label = "MD5";
			private const string Sha1Label = "SHA1";
			private const string Sha256Label = "SHA256";

			// File Info
			public readonly long? FileSize;

			// Hashes
			public readonly uint Crc32Hash;
			public readonly byte[] Md5Hash;
			public readonly byte[] Sha1Hash;
			public readonly byte[] Sha256Hash;

			// Hash String Representations
			public readonly string Crc32String;
			public readonly string Md5String;
			public readonly string Sha1String;
			public readonly string Sha256String;

			public FileHashCollection(string filePath)
			{
				byte[] bytes = File.ReadAllBytes(filePath);

				FileSize = bytes.LongLength;
				if (FileSize > 0)
					Crc32Hash = Crc32.ComputeChecksum(bytes);
				Md5Hash = MD5.Create().ComputeHash(bytes);
				Sha1Hash = SHA1.Create().ComputeHash(bytes);
				Sha256Hash = SHA256.Create().ComputeHash(bytes);

				Crc32String = Crc32Hash.ToString("x8");
				Md5String = Md5Hash.ToHexString();
				Sha1String = Sha1Hash.ToHexString();
				Sha256String = Sha256Hash.ToHexString();
			}

			public FileHashCollection(long? fileSize, string crc32, string md5, string sha1, string sha256)
			{
				FileSize = fileSize;
				Crc32String = crc32;
				Md5String = md5;
				Sha1String = sha1;
				Sha256String = sha256;
			}

			public void WriteToFile(string filePath)
			{
				using (StreamWriter sw = File.CreateText(filePath))
				{
					sw.WriteLine($"{SizeLabel}: {FileSize}");
					sw.WriteLine($"{Crc32Label}: {Crc32String}");
					sw.WriteLine($"{Md5Label}: {Md5String}");
					sw.WriteLine($"{Sha1Label}: {Sha1String}");
					sw.WriteLine($"{Sha256Label}: {Sha256String}");
				}
			}

			public static FileHashCollection ReadFromFile(string filePath, string contentPath = null)
			{
				if (!File.Exists(filePath))
				{
					if (!File.Exists(contentPath))
					{
						Log.Instance.Error($"Unable to read hashes from the file '{filePath}'. The content file '{contentPath}' does not exist either.");
						return null;
					}
					Log.Instance.Error($"Unable to read hashes from the file '{filePath}'. Recalculating from the content file '{contentPath}'.");
					return new FileHashCollection(contentPath);
				}

				long size = -1;
				string crc32 = "";
				string md5 = "";
				string sha1 = "";
				string sha256 = "";
				foreach (string line in File.ReadAllLines(filePath))
				{
					string[] lineParts = line.Split(':');
					if (lineParts.Length < 2)
						continue;
					switch (lineParts[0])
					{
						case SizeLabel:
							long.TryParse(lineParts[1].Trim(), out size);
							break;
						case Crc32Label:
							crc32 = lineParts[1].Trim();
							if (contentPath != null)
							{
								byte[] fileBytes = File.ReadAllBytes(contentPath);
								string crc32DoubleCheck = (fileBytes.LongLength > 0 ? Crc32.ComputeChecksum(fileBytes) : 0).ToString("x8");
								if (crc32DoubleCheck != crc32)
								{
									Log.Instance.Error($"CRC32 hash from the check file '{filePath}' does not match the calculated CRC32 hash of '{contentPath}'. Recalculating everything. ({crc32} != {crc32DoubleCheck})");
									return new FileHashCollection(contentPath);
								}
							}
							break;
						case Md5Label:
							md5 = lineParts[1].Trim();
							break;
						case Sha1Label:
							sha1 = lineParts[1].Trim();
							break;
						case Sha256Label:
							sha256 = lineParts[1].Trim();
							break;
					}
				}

				return new FileHashCollection(size, crc32, md5, sha1, sha256);
			}
		}

		public static byte[] CalcSha1Hash(string path)
			=> SHA1.Create().ComputeHash(File.ReadAllBytes(path));
	}
}
