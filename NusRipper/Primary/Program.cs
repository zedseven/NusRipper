using System;
using System.IO;
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
						await Ripper.DownloadFromList(args[1], args[2], concurrentThreads);
					else
						await Ripper.DownloadFromList(args[1], args[2]);
					break;
				case "decrypt":
					byte[] commonKey = await File.ReadAllBytesAsync(args[1]);
					if (args.Length >= 4)
						await Decryptor.DecryptEntries(commonKey, args[2], args[3].ToLower().Trim() == "true");
					else
						await Decryptor.DecryptEntries(commonKey, args[2]);
					break;
				case "dom":
					if (args.Length >= 5)
						await DatOMatic.BuildXmlFromFiles(args[1], args[2], args[3], args[4].ToLower().Trim() == "true");
					else if (args.Length >= 4)
						await DatOMatic.BuildXmlFromFiles(args[1], args[2], args[3]);
					else
						await DatOMatic.BuildXmlFromFiles(args[1], args[2]);
					break;
				default:
					PrintHelp();
					break;
			}
		}

		private static void PrintHelp()
		{
			Console.WriteLine("CLI Options:");
			Console.WriteLine("\tlist <listPath> <downloadDir> <maxThreads = 8> - Downloads all files listed in the file at listPath. Each line should be of the format '<titleId> <fileName>...'.");
			Console.WriteLine("\tdecrypt <commonKeyPath> <archiveDir> <makeQolFiles = false> - Decrypts all files in the downloaded 'archive' of title directories.");
			Console.WriteLine("\tdom <archiveDir> <outputFilePath> - Builds a No-Intro DAT-o-MATIC XML file from the files in the archiveDir.");
		}
	}
}
