using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NusKeys;

namespace NusRipper
{
	public static class Decryptor
	{
		private static readonly string[] KeyGenTryPasswords = { "nintendo", "mypass", "FB10", "test", "password", "twilight", "twl", "shop" };

		private const int GameTitleOffset = 0x00000000;
		private const int GameTitleSize = 12;
		private const int GameCodeOffset = 0x0000000C;
		private const int GameCodeSize = 4;
		private const int TitleInfoAddressOffset = 0x00000068;
		private const int TitleInfoAddressSize = 4;
		private const int TitleInfoTitlesOffset = 0x00000240;
		private const int TitleInfoTitleSize = 0x100;
		private const int HeaderChecksumOffset = 0x0000015E;

		public class RomInfo
		{
			public readonly string GameTitle;
			public readonly string GameCode;

			public readonly string[] Titles = new string[8];

			public const int TitleJapaneseIndex = 0;
			public const int TitleEnglishIndex = 1;
			public const int TitleFrenchIndex = 2;
			public const int TitleGermanIndex = 3;
			public const int TitleItalianIndex = 4;
			public const int TitleSpanishIndex = 5;
			public const int TitleChineseIndex = 6;
			public const int TitleKoreanIndex = 7;

			public readonly bool ValidContent;

			// TODO: Could be cool to have icon stored here too

			public RomInfo(string decPath)
			{
				byte[] contentBytes = File.ReadAllBytes(decPath);

				ushort headerChecksum = CalcCrc16Modbus(contentBytes.Slice(0, HeaderChecksumOffset, false));
				ushort romChecksumValue = BitConverter.ToUInt16(contentBytes.Slice(HeaderChecksumOffset, 2).Reverse().ToArray());
				ValidContent = headerChecksum == romChecksumValue;
				if (!ValidContent)
					return;

				GameTitle = new string(contentBytes.Slice(GameTitleOffset, GameTitleSize, false).Select(b => (char) b).ToArray()).TrimEnd('\0');
				GameCode = new string(contentBytes.Slice(GameCodeOffset, GameCodeSize, false).Select(b => (char) b).ToArray());

				int titleInfoAddress = BitConverter.ToInt32(contentBytes.Slice(TitleInfoAddressOffset, TitleInfoAddressSize, true).Reverse().ToArray());

				if (titleInfoAddress == 0x00000000)
					return;

				UnicodeEncoding encoding = new UnicodeEncoding(false, false);
				for (int i = 0; i < Titles.Length; i++)
					Titles[i] = encoding.GetString(contentBytes.Slice(titleInfoAddress + TitleInfoTitlesOffset + TitleInfoTitleSize * i, TitleInfoTitleSize, false));
			}
		}

		public static async Task<(TicketBooth.Ticket ticket, List<string> contentsList)> MakeTicketAndDecryptMetadataContents(byte[] titleIdBytes, Ripper.RudimentaryMetadata metadata, string titleDir, bool makeQolFiles = false)
		{
			if (metadata.NumContents <= 0)
				return (null, new List<string>());

			List<string> contentsList = new List<string>();
			TicketBooth.Ticket ticket = null;
			bool success = false;
			string contentPath = Path.Combine(titleDir, metadata.ContentInfo[0].Id.ToString("x8"));
			string appPath = "";
			foreach (string tryPass in KeyGenTryPasswords)
			{
				byte[] titleKey = TitleKeyGen.Derive(titleIdBytes, tryPass);
				ticket = new TicketBooth.Ticket(titleKey);
				appPath = await ticket.DecryptContent(metadata.ContentInfo[0].Index, contentPath);

				RomInfo cInfo = new RomInfo(appPath);
				success = cInfo.ValidContent;
				if (!success)
					continue;
				//NonBlockingConsole.WriteLine($"'{tryPass}' is the password for the title key of '{contentPath}'!");
				contentsList.Add(metadata.ContentInfo[0].Id.ToString("x8"));
				if (makeQolFiles)
					MakeQolFiles(cInfo, titleDir);
				break;
			}

			if (!success)
			{
				NonBlockingConsole.WriteLine($"Unable to find the password for the title key of '{contentPath}'.");
				File.Delete(appPath);
				return (null, new List<string>());
			}

			for (int i = 1; i < metadata.NumContents; i++)
			{
				string contentName = metadata.ContentInfo[i].Id.ToString("x8");
				contentsList.Add(contentName);
				contentPath = Path.Combine(titleDir, contentName);
				await ticket.DecryptContent(metadata.ContentInfo[i].Index, contentPath);
			}

			return (ticket, contentsList);
		}

		public static async Task<List<string>> DecryptMetadataContents(TicketBooth.Ticket ticket, Ripper.RudimentaryMetadata metadata, string titleDir, bool makeQolFiles = false)
		{
			if (metadata.NumContents <= 0)
				return new List<string>();

			List<string> contentsList = new List<string>();
			for (int i = 0; i < metadata.NumContents; i++)
			{
				string contentName = metadata.ContentInfo[i].Id.ToString("x8");
				contentsList.Add(contentName);
				string contentPath = Path.Combine(titleDir, contentName);
				string appPath = await ticket.DecryptContent(metadata.ContentInfo[i].Index, contentPath);
				if (i != 0 || !makeQolFiles)
					continue;
				RomInfo cInfo = new RomInfo(appPath);
				MakeQolFiles(cInfo, titleDir);
			}

			return contentsList;
		}

		public static void MakeQolFiles(RomInfo info, string titleDir)
		{
			string gameTitle = "";
			if (info.GameCode == null)
				return;
			if (info.Titles[RomInfo.TitleEnglishIndex] != null)
				gameTitle = Helpers.GetSafeFilename($"{info.GameCode} - {info.GameTitle} - {info.Titles[RomInfo.TitleEnglishIndex].Replace('\n', ' ')}.txt");
			else if (info.GameTitle != null)
				gameTitle = Helpers.GetSafeFilename($"{info.GameCode} - {info.GameTitle}.txt");
			else
				gameTitle = Helpers.GetSafeFilename($"{info.GameCode}.txt");
			using (File.Create(Path.Combine(titleDir, gameTitle))) { }
		}

		private static ushort CalcCrc16Modbus(byte[] bytes)
		{
			ushort crc = 0xFFFF;

			for (int pos = 0; pos < bytes.Length; pos++)
			{
				crc ^= bytes[pos];

				for (int i = 8; i != 0; i--)
				{
					if ((crc & 0x0001) != 0)
					{
						crc >>= 1;
						crc ^= 0xA001;
					}
					else
						crc >>= 1;
				}
			}
			return crc;
		}
	}
}
