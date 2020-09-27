using System;
using System.IO;
using System.Linq;
using System.Net.Http;
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
				default:
					PrintHelp();
					break;
			}
		}

		private static async Task DownloadFromList(string listPath, string downloadDir, int concurrentThreads = 4)
		{
			string[] listLines = await File.ReadAllLinesAsync(listPath);
			HttpClient client = new HttpClient();
			Parallel.ForEach(listLines, new ParallelOptions { MaxDegreeOfParallelism = concurrentThreads }, async line =>
			{
				string[] lineParts = line.Split(' ').Select(l => l.Trim()).ToArray();
				if (lineParts.Length < 2)
					return;
				Console.WriteLine(line);
				string titleDir = Path.Combine(downloadDir, lineParts[0]);
				Directory.CreateDirectory(titleDir);
				await Ripper.DownloadTitleFile(client, titleDir, lineParts[0], lineParts[1]);
			});
		}

		private static void PrintHelp()
		{
			Console.WriteLine("CLI Options:");
			Console.WriteLine("\tlist <listPath> <downloadDir> [concurrentThreads = 4] - Downloads all files listed in the file at listPath. Each line should be of the format '<titleId> <fileName>...'.");
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
