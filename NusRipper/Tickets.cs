using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NusKeys;

namespace NusRipper
{
	public static class TicketBooth
	{
		private const int TitleKeyOffset = 0x000001BF;

		public class Ticket
		{
			public readonly byte[] titleKey;

			public Ticket(byte[] commonKey, string titleId, string ticketPath)
			{
				byte[] ticketBytes = File.ReadAllBytes(ticketPath);

				byte[] encTitleKey = ticketBytes.Slice(TitleKeyOffset, 16, false);

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

			public async Task<string> DecryptContent(short contentIndex, string encPath, string decPath = null)
			{
				decPath ??= $"{encPath}.app";

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
	}
}
