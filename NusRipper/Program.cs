using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NusRipper
{
	public sealed class Program
	{
		private static async Task Main(string[] args)
		{
			if (args.Length < 1)
			{
				PrintHelp();
				return;
			}

			switch (args[0])
			{
				case "list":
					if(args.Length >= 4 && int.TryParse(args[3].Trim(), out int concurrentThreads))
						await DownloadFromList(args[1], args[2], concurrentThreads);
					else
						await DownloadFromList(args[1], args[2]);
					break;
				case "decrypt":
					byte[] commonKey = await File.ReadAllBytesAsync(args[1]);
					if (args.Length >= 4)
						await DecryptEntries(commonKey, args[2], args[3].ToLower().Trim() == "true");
					else
						await DecryptEntries(commonKey, args[2]);
					break;
				default:
					PrintHelp();
					break;
			}
		}

		private static async Task DownloadFromList(string listPath, string downloadDir, int maxThreads = -1)
		{
			string[] listLines = await File.ReadAllLinesAsync(listPath);
			HttpClient client = new HttpClient();
			Parallel.ForEach(listLines, new ParallelOptions { MaxDegreeOfParallelism = maxThreads }, async line =>
			{
				string[] lineParts = line.Split(' ').Select(l => l.Trim()).ToArray();
				if (lineParts.Length < 2)
					return;
				NonBlockingConsole.WriteLine(line);
				string titleDir = Path.Combine(downloadDir, lineParts[0]);
				Directory.CreateDirectory(titleDir);
				await Ripper.DownloadTitleFile(client, titleDir, lineParts[0], lineParts[1]);
			});
		}

		private static async Task DecryptEntries(byte[] commonKey, string archiveDir, bool makeQolFiles = false/*, int maxThreads = -1*/)
		{
			//Parallel.ForEach(Directory.EnumerateDirectories(archiveDir), new ParallelOptions { MaxDegreeOfParallelism = maxThreads }, async titleDir =>
			foreach(string titleDir in Directory.EnumerateDirectories(archiveDir))
			{
				string titleId = Path.GetFileName(titleDir);

				TicketBooth.Ticket ticket = null;
				List<string> decryptedContents = new List<string>();
				Regex metadataFileRegex = new Regex(Ripper.MetadataFileName + @"(?:\.\d+)?$");
				IEnumerable<string> metadataFiles = Directory.EnumerateFiles(titleDir).Where(p => metadataFileRegex.IsMatch(p));
				string ticketPath = Path.Combine(titleDir, Ripper.TicketFileName);
				if (File.Exists(ticketPath))
				{
					//NonBlockingConsole.WriteLine($"A ticket exists for '{titleDir}'.");
					ticket = new TicketBooth.Ticket(commonKey, titleId, ticketPath);
					foreach (string metadataPath in metadataFiles)
						decryptedContents.AddRange(await Decryptor.DecryptMetadataContents(ticket, new Ripper.RudimentaryMetadata(metadataPath), titleDir, makeQolFiles));
				}
				else
				{
					foreach (string metadataPath in metadataFiles)
					{
						if(ticket == null)
						{
							(TicketBooth.Ticket ticket, List<string> contentsList) res =
								await Decryptor.MakeTicketAndDecryptMetadataContents(Helpers.HexStringToBytes(titleId),
									new Ripper.RudimentaryMetadata(metadataPath), titleDir, makeQolFiles);
							ticket = res.ticket;
							decryptedContents.AddRange(res.contentsList);
						}
						else
							decryptedContents.AddRange(await Decryptor.DecryptMetadataContents(ticket, new Ripper.RudimentaryMetadata(metadataPath), titleDir, makeQolFiles));
					}
				}

				if (ticket == null)
					continue;

				Regex contentFileRegex = new Regex(@"\d{8}$");
				IEnumerable<string> contentFiles = Directory.EnumerateFiles(titleDir).Select(Path.GetFileName).Where(p => contentFileRegex.IsMatch(p));

				IEnumerable<string> remainingContents = contentFiles.Except(decryptedContents);
				foreach (string contentName in remainingContents)
				{
					string contentPath = Path.Combine(titleDir, contentName);
					NonBlockingConsole.WriteLine($"Attempting to decrypt content without associated metadata: '{contentPath}'");
					await ticket.DecryptContent(0, contentPath);
				}
			}//);
		}

		private static void PrintHelp()
		{
			NonBlockingConsole.WriteLine("CLI Options:");
			NonBlockingConsole.WriteLine("\tlist <listPath> <downloadDir> <maxThreads = -1> - Downloads all files listed in the file at listPath. Each line should be of the format '<titleId> <fileName>...'.");
			NonBlockingConsole.WriteLine("\tdecrypt <commonKeyPath> <archiveDir> <makeQolFiles = false> - Decrypts all files in the downloaded 'archive' of title directories.");
		}

		private static string BytesToHex(byte[] bytes)
		{
			string ret = "";
			for (int i = 0; i < bytes.Length; i++)
				ret += bytes[i].ToString("X2");
			return ret;
		}
	}
}
