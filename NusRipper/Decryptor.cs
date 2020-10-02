using NusKeys;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NusRipper
{
	public static class Decryptor
	{
		private static readonly string[] KeyGenTryPasswords = { "nintendo", "mypass", "FB10", "test", "", "password", "twilight", "twl", "shop" };

		public static async Task<(TicketBooth.Ticket ticket, List<string> contentsList)> MakeTicketAndDecryptMetadataContents(byte[] titleIdBytes, TitleMetadata metadata, string titleDir, bool makeQolFiles = false)
		{
			if (metadata.NumContents <= 0)
				return (null, new List<string>());

			List<string> contentsList = new List<string>();
			TicketBooth.Ticket ticket = null;
			bool success = false;
			string contentPath = Path.Combine(titleDir, metadata.ContentInfo[0].Id.ToString("x8"));
			string appPath = "";
			// TODO: Not needed for any known DSiWare titles, but could be a decent idea to test the gamecode of the game as a password
			foreach (string tryPass in KeyGenTryPasswords)
			{
				byte[] titleKey = TitleKeyGen.Derive(titleIdBytes, tryPass);
				ticket = new TicketBooth.Ticket(titleKey);
				appPath = await ticket.DecryptContent(metadata.ContentInfo[0].Index, contentPath);

				RomInfo cInfo = new RomInfo(appPath, makeQolFiles);
				success = cInfo.ValidContent;
				if (!success)
					continue;
				Log.Instance.Trace($"'{tryPass}' is the password for the title key of '{contentPath}'!");

				// Somewhat redundant given the CRC validation of the decrypted ROM in RomInfo, but still
				VerifyMetadataContent(metadata, appPath, 0, titleDir, metadata.ContentInfo[0].Id.ToString("x8"));

				contentsList.Add(metadata.ContentInfo[0].Id.ToString("x8"));
				if (makeQolFiles)
					MakeQolFiles(cInfo, titleDir);
				break;
			}

			if (!success)
			{
				Log.Instance.Error($"Unable to find the password for the title key of '{contentPath}'.");
				File.Delete(appPath);
				return (null, new List<string>());
			}

			for (int i = 1; i < metadata.NumContents; i++)
			{
				string contentName = metadata.ContentInfo[i].Id.ToString("x8");
				contentsList.Add(contentName);
				contentPath = Path.Combine(titleDir, contentName);
				string decPath = await ticket.DecryptContent(metadata.ContentInfo[i].Index, contentPath);
				VerifyMetadataContent(metadata, decPath, i, titleDir, contentName);
			}

			return (ticket, contentsList);
		}

		public static async Task<List<string>> DecryptMetadataContents(TicketBooth.Ticket ticket, TitleMetadata metadata, string titleDir, bool makeQolFiles = false)
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

				// TODO: Could verify enc and dec file sizes match metadata recorded size, but at this point I don't think it's worth the additional processing time.

				VerifyMetadataContent(metadata, appPath, i, titleDir, contentName);

				if (i != 0 || !makeQolFiles)
					continue;
				RomInfo cInfo = new RomInfo(appPath, makeQolFiles);
				MakeQolFiles(cInfo, titleDir);
			}

			return contentsList;
		}

		public static bool VerifyMetadataContent(TitleMetadata metadata, string decPath, int contentIndex, string titleDir, string contentIdStr)
		{
			byte[] decHash = Hasher.CalcSha1Hash(decPath);
			if (!metadata.ContentInfo[contentIndex].DecSha1Hash.SequenceEqual(decHash))
			{
				Log.Instance.Error(
					$"Hash for '{decPath}' does not match the recorded hash in '{Path.Combine(titleDir, metadata.FileName)}' (content index {metadata.ContentInfo[contentIndex].Index}). " +
					$"(`{metadata.ContentInfo[contentIndex].DecSha1Hash.ToHexString()}` != `{decHash.ToHexString()}`)");
				return false;
			}
			else
			{
				Log.Instance.Trace(
					$"Hash for '{decPath}' matches the recorded hash in '{Path.Combine(titleDir, metadata.FileName)}' (content index {metadata.ContentInfo[contentIndex].Index}). " +
					$"(`{metadata.ContentInfo[contentIndex].DecSha1Hash.ToHexString()}`)");
				return true;
			}
		}

		public static void MakeQolFiles(RomInfo info, string titleDir)
		{
			if (info.GameCode == null)
				return;
			using (File.Create(Path.Combine(titleDir, Helpers.GetSafeFilename($"{info.GetProperName(true)}.txt")))) { }

			info.StaticIcon?.SaveAsPng(Path.Combine(titleDir, "icon.png"));
			info.AnimatedIcon?.SaveAsGif(Path.Combine(titleDir, "icon.gif"), new GifEncoder { ColorTableMode = GifColorTableMode.Local });
		}
	}
}
