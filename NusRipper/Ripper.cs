using System;
using System.Collections.Generic;
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
		private const string DownloadUrlBase = "http://nus.cdn.t.shop.nintendowifi.net/ccs/download/";

		public static bool DownloadSuffixlessMetadata = false;

		private const string TicketFileName = "cetk";
		private const string MetadataFileName = "tmd";

		private const string HeaderFileSuffix = ".headers.txt";
		private const string HashesFileSuffix = ".checks.txt";

		private const int NumContentsOffset = 0x000001DE;
		private const int ContentsListOffset = 0x000001E4;
		private const int BytesPerContentChunk = 36;

		private class RudimentaryMetadata
		{
			public class RudimentaryContentInfo
			{
				public int Id;
				public short Index;

				public RudimentaryContentInfo(int id, short index)
				{
					Id = id;
					Index = index;
				}
			}

			public readonly short NumContents;
			public readonly RudimentaryContentInfo[] ContentInfo;

			public RudimentaryMetadata(short numContents, RudimentaryContentInfo[] contentInfo)
			{
				NumContents = numContents;
				ContentInfo = contentInfo;
			}
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
				RudimentaryMetadata metadata = GetNecessaryMetadataInfo(metadataPath);
				for (int i = 0; i < metadata.NumContents; i++)
					await DownloadTitleFile(client, metadataDir, titleId, metadata.ContentInfo[i].Id.ToString("x8"));
			}
		}

		public static async Task<string> DownloadTitleFile(HttpClient client, string downloadDir, string titleId, string file)
		{
			string downloadUrl = $"{DownloadUrlBase}{titleId}/{file}";

			HttpResponseMessage response = client.GetAsync(downloadUrl).Result;

			string fileDownloadPath = Path.Combine(downloadDir, file);
			string headerDownloadPath = Path.Combine(downloadDir, $"{file}{HeaderFileSuffix}");
			string hashesDownloadPath = Path.Combine(downloadDir, $"{file}{HashesFileSuffix}");

			if (!response.IsSuccessStatusCode)
				return null;

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

			Hasher.FileHashCollection coll = new Hasher.FileHashCollection(fileDownloadPath);
			coll.WriteToFile(hashesDownloadPath);

			return fileDownloadPath;
		}

		private static RudimentaryMetadata GetNecessaryMetadataInfo(string metadataPath)
		{
			byte[] bytes = File.ReadAllBytes(metadataPath);

			short numContents = BitConverter.ToInt16(bytes.Slice(NumContentsOffset, 2));
			List<RudimentaryMetadata.RudimentaryContentInfo> contentInfo = new List<RudimentaryMetadata.RudimentaryContentInfo>();
			for (int i = 0; i < numContents; i++)
				contentInfo.Add(new RudimentaryMetadata.RudimentaryContentInfo(
					BitConverter.ToInt32(bytes.Slice(ContentsListOffset + i * BytesPerContentChunk, 4)),
					BitConverter.ToInt16(bytes.Slice(ContentsListOffset + i * BytesPerContentChunk + 4, 2))));

			return new RudimentaryMetadata(numContents, contentInfo.ToArray());
		}
	}
}
