using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace NusRipper
{
	public static class TicketBooth
	{
		public const string DecryptedContentExtension = "app";
		private const int TitleKeyOffset = 0x000001BF;
		private const int VersionOffset = 0x000001E6;

		public class Ticket
		{
			private readonly byte[] titleKey;

			public Ticket(byte[] commonKey, string titleId, string ticketPath)
			{
				byte[] ticketBytes = File.ReadAllBytes(ticketPath);

				byte[] encTitleKey = ticketBytes.Slice(TitleKeyOffset, 16);

				Aes aes = new AesManaged
				{
					Mode = CipherMode.CBC,
					Padding = PaddingMode.None,
					KeySize = 128,
					BlockSize = 128,
					Key = commonKey,
					IV = Helpers.HexStringToBytes(titleId).Pad(16)
				};

				using (MemoryStream ms = new MemoryStream())
				using (CryptoStream cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
				{
					cs.Write(encTitleKey);
					cs.FlushFinalBlock();

					titleKey = ms.ToArray();
				}
			}

			public Ticket(byte[] decTitleKey)
				=> titleKey = decTitleKey;

			public async Task<string> DecryptContent(ushort contentIndex, string encPath, string decExt = DecryptedContentExtension)
			{
				string decPath = $"{encPath}.{decExt}";

				Aes aes = new AesManaged
				{
					Mode = CipherMode.CBC,
					Padding = PaddingMode.None,
					KeySize = 128,
					BlockSize = 128,
					Key = titleKey,
					IV = BitConverter.IsLittleEndian ? BitConverter.GetBytes(contentIndex).Reverse().ToArray().Pad(16) : BitConverter.GetBytes(contentIndex).Pad(16)
				};

				await using (FileStream efs = File.OpenRead(encPath))
				await using (FileStream dfs = File.Create(decPath))
				await using (CryptoStream cs = new CryptoStream(dfs, aes.CreateDecryptor(), CryptoStreamMode.Write))
				{
					await efs.CopyToAsync(cs);
					cs.FlushFinalBlock();
				}

				return decPath;
			}
		}

		public static ushort GetTicketVersion(string ticketPath)
			=> BitConverter.ToUInt16(
				File.ReadAllBytes(ticketPath)
					.Slice(VersionOffset, 2, true));
	}
}
