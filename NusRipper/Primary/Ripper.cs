using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace NusRipper
{
	public static class Ripper
	{
		/// <summary>
		/// The base of the CDN download URL. The final URL for an entry follows the format of 'http://nus.cdn.t.shop.nintendowifi.net/ccs/download/[titleid]/[file]'.
		/// </summary>
		private const string DownloadUrlBase = "http://nus.cdn.t.shop.nintendowifi.net/ccs/download";

		public static bool DownloadSuffixlessMetadata = false;

		public const string TicketFileName = "cetk";
		public const string MetadataFileName = "tmd";

		public const string HeaderFileSuffix = ".headers.txt";
		public const string HashesFileSuffix = ".checks.txt";

		private const int MaxDownloadAttempts = 5;

		internal static async Task DownloadFromList(string listPath, string downloadDir, int maxThreads = 8)
		{
			maxThreads = maxThreads < -1 ? -1 : maxThreads;
			maxThreads = maxThreads == 0 ? 1 : maxThreads;

			Stopwatch stopwatch = Stopwatch.StartNew();
			Log.Instance.Info($"Beginning download from the list '{listPath}' with {(maxThreads > -1 ? maxThreads.ToString() : "unlimited")} max thread{(maxThreads != 1 ? "s" : "")}.");
			
			string[] listLines = await File.ReadAllLinesAsync(listPath);
			HttpClient client = new HttpClient();
			Parallel.ForEach(listLines, new ParallelOptions { MaxDegreeOfParallelism = maxThreads }, async line =>
			{
				string[] lineParts = line.Split(' ').Select(l => l.Trim()).ToArray();
				if (lineParts.Length < 2)
					return;
				Log.Instance.Info(line);
				string titleDir = Path.Combine(downloadDir, lineParts[0]);
				Directory.CreateDirectory(titleDir);
				await DownloadTitleFile(client, titleDir, lineParts[0], lineParts[1]);
			});

			Log.Instance.Info($"Completed the download from the list in {stopwatch.ElapsedAfterStopped().ToNiceString()}.");
		}

		public static async Task DownloadTitle(HttpClient client, string downloadDir, string titleId, params int[] metadataVersions)
		{
			string titleDir = Path.Combine(downloadDir, titleId);
			Directory.CreateDirectory(titleDir);

			// Download Common ETicKet (CETK)
			await DownloadTitleFile(client, titleDir, titleId, TicketFileName);

			// Download Title MetaData (TMD)
			string[] metadataNames = DownloadSuffixlessMetadata ?
				metadataVersions.Select(v => $"{MetadataFileName}.{v}").Union(new[] { MetadataFileName }).ToArray() :
				metadataVersions.Select(v => $"{MetadataFileName}.{v}").ToArray();
			foreach (string metadataName in metadataNames)
			{
				string metadataDir = Path.Combine(titleDir, metadataName);
				Directory.CreateDirectory(metadataDir);

				string metadataPath = await DownloadTitleFile(client, metadataDir, titleId, metadataName);
				if(metadataPath == null)
					continue;

				// Parse out the content IDs from the metadata file, and download them
				TitleMetadata metadata = new TitleMetadata(metadataPath);
				for (int i = 0; i < metadata.NumContents; i++)
					await DownloadTitleFile(client, metadataDir, titleId, metadata.ContentInfo[i].Id.ToString("x8"));
			}
		}

		public static async Task<string> DownloadTitleFile(HttpClient client, string downloadDir, string titleId, string file)
		{
			string downloadUrl = $"{DownloadUrlBase}/{titleId}/{file}";

			string fileDownloadPath = Path.Combine(downloadDir, file);
			string headerDownloadPath = Path.Combine(downloadDir, $"{file}{HeaderFileSuffix}");
			string hashesDownloadPath = Path.Combine(downloadDir, $"{file}{HashesFileSuffix}");

			//Log.Instance.Trace($"Threads in use: {Process.GetCurrentProcess().Threads.Count}");
			//HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, downloadUrl) {Headers = { ConnectionClose = true }};
			for(int i = 0; i < MaxDownloadAttempts; i++)
			{
				try
				{
					using HttpResponseMessage response = client.GetAsync(downloadUrl/*, HttpCompletionOption.ResponseHeadersRead*/).Result;
					//HttpResponseMessage response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).Result;

					if (!response.IsSuccessStatusCode)
					{
						Log.Instance.Error($"Received the following HTTP response code for '{downloadUrl}': {(int) response.StatusCode} {response.StatusCode}");
						return null;
					}

					await using (FileStream fs = new FileStream(fileDownloadPath, FileMode.Create))
					await using (StreamWriter sw = File.CreateText(headerDownloadPath))
					{
						// Write interaction headers
						string headerContents = Enumerable
							.Empty<(string name, string value)>()
							.Concat(
								response.Headers
									.SelectMany(kvp => kvp.Value
										.Select(v => (name: kvp.Key, value: v))
									)
							)
							.Concat(
								response.Content.Headers
									.SelectMany(kvp => kvp.Value
										.Select(v => (name: kvp.Key, value: v))
									)
							)
							.Aggregate(
								new StringBuilder(),
								(sb, pair) => sb.Append(pair.name).Append(": ").Append(pair.value).AppendLine(),
								sb => sb.ToString()
							);

						await sw.WriteAsync(headerContents);
						await sw.FlushAsync();

						// Write file contents
						await response.Content.CopyToAsync(fs);
						await fs.FlushAsync();
					}
				}
				// I'm aware catch-all clauses are bad practice, but due to the yet-unfixed .NET bug https://github.com/dotnet/runtime/issues/23870
				// race conditions cause a plethora of exception types that can't all be predicted and often aren't very descriptive.
				// ReSharper disable once CatchAllClause
				catch (Exception e)
				{
					Log.Instance.Error($"Download failed for '{downloadUrl}' (Attempt #{i + 1}) - Exception: '{e.Message}' - Retrying...");
					if (i >= MaxDownloadAttempts - 1)
					{
						Log.Instance.Error($"Max number of download attempts ({MaxDownloadAttempts}) reached for this file. Giving up and moving on.");
						return null;
					}
					continue;
				}

				break;
			}

			Hasher.FileHashCollection coll = new Hasher.FileHashCollection(fileDownloadPath);
			coll.WriteToFile(hashesDownloadPath);

			return fileDownloadPath;
		}
	}
}
