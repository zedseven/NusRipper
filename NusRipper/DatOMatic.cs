using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

namespace NusRipper
{
	internal static class DatOMatic
	{
		private const string LastModifiedLabel = "Last-Modified";

		/// <summary>
		/// The canonical order that languages should be listed in for DAT-o-MATIC dat files.<br />
		/// As per: <a href='https://datomatic.no-intro.org/stuff/The%20Official%20No-Intro%20Convention%20(20071030).pdf'>https://datomatic.no-intro.org/stuff/The%20Official%20No-Intro%20Convention%20(20071030).pdf</a>
		/// </summary>
		private static readonly List<Languages.LanguageCode> LanguageOrder = new List<Languages.LanguageCode>(new []
		{
			Languages.English,
			Languages.Japanese,
			Languages.French,
			Languages.German,
			Languages.Spanish,
			Languages.Italian,
			Languages.Dutch,
			Languages.Portuguese,
			Languages.Swedish,
			Languages.Norwegian,
			Languages.Danish,
			Languages.Finnish,
			Languages.Chinese,
			Languages.Korean,
			Languages.Polish
		});

		[Pure]
		public static Regions.Region RegionFromCharCode(char charCode)
			=> charCode switch
			{
				'E' => Regions.Region.USA,
				'J' => Regions.Region.Japan,
				'P' => Regions.Region.Europe,
				'U' => Regions.Region.Australia,
				'K' => Regions.Region.Korea,
				'V' => Regions.Region.Europe | Regions.Region.Australia,
				'C' => Regions.Region.China,
				'D' => Regions.Region.Germany,
				'F' => Regions.Region.France,
				'I' => Regions.Region.Italy,
				'S' => Regions.Region.Spain,
				'O' => Regions.Region.USA | Regions.Region.Europe,
				'X' => Regions.Region.Europe, // originally "Unknown" - all 4 DSiWare titles that use it have GB,FR,DE,IT,ES shop pages, though often don't use GB or ES
				'T' => Regions.Region.USA | Regions.Region.Australia,
				'H' => Regions.Region.Netherlands,
				'A' => Regions.Region.World,
				_ => Regions.Region.World
			};

		internal static async Task BuildXmlFromFiles(string archiveDir, string outputXmlPath, string outputMediaPath = null)
		{
			Stopwatch stopwatch = Stopwatch.StartNew();

			bool writeMedia = outputMediaPath != null;

			Log.Instance.Info(writeMedia
				? $"Beginning a build of a DAT-o-MATIC XML dat file of '{archiveDir}', writing to '{outputXmlPath}' and '{outputMediaPath}'."
				: $"Beginning a build of a DAT-o-MATIC XML dat file of '{archiveDir}', writing to '{outputXmlPath}'.");

			XmlWriterSettings settings = new XmlWriterSettings
			{
				Async = true,
				CloseOutput = true,
				ConformanceLevel = ConformanceLevel.Auto,
				Encoding = Encoding.UTF8,
				Indent = true,
				NewLineChars = "\r\n",
				OmitXmlDeclaration = false,
				WriteEndDocumentOnClose = true
			};

			await using (FileStream fs = new FileStream(outputXmlPath, FileMode.Create))
			using (XmlWriter writer = XmlWriter.Create(fs, settings))
			{
				await writer.WriteStartDocumentAsync();

				await writer.WriteRawAsync("\r\n<!DOCTYPE datafile PUBLIC \"http://www.logiqx.com/Dats/datafile.dtd\" \"-//Logiqx//DTD ROM Management Datafile//EN\">\r\n");

				writer.WriteStartElement("datafile");
				writer.WriteStartElement("header");
				await writer.WriteEndElementAsync();
				foreach (string titleDir in Directory.EnumerateDirectories(archiveDir))
				{
					// Manage basic Title ID stuff
					string titleId = Path.GetFileName(titleDir);
					string titleIdLower = titleId.ToLower();
					string titleIdGameCode = RomInfo.DeriveGameCodeFromTitleId(titleId);

					Log.Instance.Info($"{titleId} - {titleIdGameCode}");

					// Parse out the files in the dir into manageable entries
					List<string> ticketFiles = new List<string>();
					List<(string fileName, TitleMetadata metadata)> metadataFiles = new List<(string fileName, TitleMetadata metadata)>();
					List<string> contentEncryptedFiles = new List<string>();
					Dictionary<string, (string fileName, RomInfo info, int metadataIndex)> contentDecryptedFiles = new Dictionary<string, (string fileName, RomInfo info, int metadataIndex)>();
					foreach (string filePath in Directory.EnumerateFiles(titleDir))
					{
						string fileName = Path.GetFileName(filePath);
						if (fileName == Ripper.TicketFileName)
						{
							ticketFiles.Add(fileName);
							continue;
						}
						if (Constants.MetadataFileRegex.IsMatch(fileName))
						{
							metadataFiles.Add((fileName, new TitleMetadata(filePath)));
							continue;
						}
						if (Constants.ContentEncryptedFileRegex.IsMatch(fileName))
						{
							contentEncryptedFiles.Add(fileName);
							continue;
						}
						if (Constants.ContentDecryptedFileRegex.IsMatch(fileName))
						{
							contentDecryptedFiles.Add(fileName.Split('.')[0], (fileName, new RomInfo(filePath), -1));
							continue;
						}
					}

					// Check for empty/malformed folders
					int totalCheckFiles = metadataFiles.Count + contentEncryptedFiles.Count + contentDecryptedFiles.Count;
					if (totalCheckFiles < 3)
					{
						Log.Instance.Error($"Dir '{titleDir}' has below the expected number of files: {totalCheckFiles}.");
						continue;
					}

					// Sort metadata files by their extension, since with alphanumeric ordering tmd.1024 comes before tmd.256 and it looks messy
					metadataFiles.Sort((m, n) =>
					{
						int dotIndex0 = m.fileName.IndexOf('.');
						int dotIndex1 = n.fileName.IndexOf('.');
						if (dotIndex0 < 0 && dotIndex1 < 0)
							return 0;
						if (dotIndex0 < 0 && dotIndex1 >= 0)
							return -1;
						if (dotIndex0 >= 0 && dotIndex1 < 0)
							return 1;
						return int.Parse(m.fileName.Substring(dotIndex0 + 1)).CompareTo(int.Parse(n.fileName.Substring(dotIndex1 + 1)));
					});

					// Store the newest version, for retrieval of specific title-wide info that can only be parsed from content
					int newestMetadataIndex = -1;
					{
						int newestMetadataVersion = -1;
						for (int i = metadataFiles.Count - 1; i >= 0; i--)
						{
							if (metadataFiles[i].metadata.TitleVersion > newestMetadataVersion)
							{
								newestMetadataVersion = metadataFiles[i].metadata.TitleVersion;
								newestMetadataIndex = i;
							}
							for (int j = 0; j < metadataFiles[i].metadata.ContentInfo.Length; j++)
							{
								string contentName = metadataFiles[i].metadata.ContentInfo[j].Id.ToString("x8");
								if (contentDecryptedFiles.ContainsKey(contentName))
									contentDecryptedFiles[contentName] = (
										contentDecryptedFiles[contentName].fileName,
										contentDecryptedFiles[contentName].info,
										i);
								else
									Log.Instance.Error($"The content file '{contentName}' does not exist, but should according to the metadata file '{metadataFiles[i].fileName}'.");
							}
						}
					}
					string newestContentId = metadataFiles[newestMetadataIndex].metadata.ContentInfo[0].Id.ToString("x8");

					// Ensure the number of encrypted titles matches the number of decrypted ones (more of a sanity check than anything)
					if (contentEncryptedFiles.Count != contentDecryptedFiles.Count)
						Log.Instance.Error($"The number of encrypted files ({contentEncryptedFiles.Count}) is mismatched with the number of decrypted ones ({contentDecryptedFiles.Count}).");

					// Region
					char regionCode = contentDecryptedFiles[newestContentId].info.RegionCode;
					if (regionCode == '\0')
					{
						Log.Instance.Warn($"The ROM belonging to title ID '{titleId}' of the filename '{contentDecryptedFiles[newestContentId].fileName}' " +
						                  "is not valid, and the gamecode for the title will be derived from the title ID.");
						regionCode = titleIdGameCode[RomInfo.GameCodeLength - 1];
					}
					Regions.Region region = RegionFromCharCode(regionCode);
					Regions.Region primaryRegion = region.GetPrimaryRegion();
					string regionStr = region.ToString(); // TODO: If this does work, nuke ToPrettyString() - if it doesn't, use ToPrettyString()

					// Languages
					HashSet<Languages.LanguageCode> languages = new HashSet<Languages.LanguageCode>();

					Tuple<string, int, int>[] titleGroups = contentDecryptedFiles[newestContentId].info.Titles
						.Select((t, i) => (t: Languages.SanitizeRomTitleForLanguageDetermination(t), i))
						.GroupBy(t => t.t)
						.Select(e => new Tuple<string, int, int>(e.Key, e.First().i, e.Count()))
						.ToArray();

					foreach (Tuple<string, int, int> titleEntry in titleGroups.Where(g => g.Item3 == 1))
					{
						switch (titleEntry.Item2)
						{
							case (int) RomInfo.TitleIndices.JapaneseIndex:
								languages.Add(Languages.Japanese);
								break;
							case (int) RomInfo.TitleIndices.EnglishIndex:
								languages.Add(Languages.English);
								break;
							case (int) RomInfo.TitleIndices.FrenchIndex:
								languages.Add(Languages.French);
								break;
							case (int) RomInfo.TitleIndices.GermanIndex:
								languages.Add(Languages.German);
								break;
							case (int) RomInfo.TitleIndices.ItalianIndex:
								languages.Add(Languages.Italian);
								break;
							case (int) RomInfo.TitleIndices.SpanishIndex:
								languages.Add(Languages.Spanish);
								break;
							case (int) RomInfo.TitleIndices.ChineseIndex:
								languages.Add(Languages.Chinese);
								break;
							case (int) RomInfo.TitleIndices.KoreanIndex:
								languages.Add(Languages.Korean);
								break;
						}
					}

					Tuple<string, int, int>[] titleConflicts = titleGroups
						.Where(g => g.Item3 > 1)
						.ToArray();
					for (int i = 0; i < titleConflicts.Length; i++)
					{
						if (string.IsNullOrWhiteSpace(titleConflicts[i].Item1))
							continue;
						string sanitizedTitle = titleConflicts[i].Item1;

						Log.Instance.Trace($"Title '{titleId}' has a title conflict for title '{sanitizedTitle}' ({titleConflicts[i].Item3} titles are the same).");

						List<Languages.LanguageCode> langs = Languages.DetermineLanguage(sanitizedTitle);
						Languages.LanguageCode[] regionLangs = Regions.GetExpectedLanguages(region);
						langs = langs.OrderBy(l => regionLangs.Contains(l) ? 0 : 1).ToList();

						bool addedLang = false;
						for (int j = 0; j < langs.Count; j++)
						{
							if (languages.Contains(langs[j]))
								continue;
							languages.Add(langs[j]);
							addedLang = true;
							if (j > 0)
								Log.Instance.Debug("The first choice of language was not chosen, as it was already defined by a known language instance. " +
								                   $"Language choice {j} (0-indexed) was chosen - {langs[j]}. The title in question: '{sanitizedTitle}'");
							break;
						}

						if (!addedLang)
							languages.Add(Regions.RegionExpectedLanguageMap[primaryRegion][0]);
					}

					if (languages.Count <= 0)
					{
						Languages.LanguageCode lang = Regions.RegionExpectedLanguageMap[primaryRegion][0];
						Log.Instance.Warn($"No languages were found or determined for the title '{titleId}'. Proceeding with the default language for the region ({region}), {lang}.");
						languages.Add(lang);
					}

					List<string> languageCodesList = languages.OrderBy(x =>
					{
						int index = LanguageOrder.IndexOf(x);
						return index > -1 ? index : int.MaxValue;
					}).Select(l => l.Code).ToList();
					string languagesStr = string.Join(", ", languageCodesList);

					// Write the XML
					writer.WriteStartElement("game");
					await writer.WriteAttributeAsync("name", titleIdLower);
					writer.WriteStartElement("archive");
					await writer.WriteAttributeAsync("name",           titleIdLower);
					await writer.WriteAttributeAsync("namealt",        "");
					await writer.WriteAttributeAsync("region",         regionStr);
					await writer.WriteAttributeAsync("languages",      languagesStr);
					await writer.WriteAttributeAsync("version",        "");
					await writer.WriteAttributeAsync("devstatus",      "");
					await writer.WriteAttributeAsync("additional",     "");
					await writer.WriteAttributeAsync("special1",       "");
					await writer.WriteAttributeAsync("special2",       "");
					await writer.WriteAttributeAsync("gameid",         "");
					await writer.WriteAttributeAsync("clone",          "");
					await writer.WriteAttributeAsync("regionalparent", "");
					await writer.WriteEndElementAsync();
					writer.WriteStartElement("flags");
					await writer.WriteAttributeAsync("bios",           "0");
					await writer.WriteAttributeAsync("licensed",       "1");
					await writer.WriteAttributeAsync("pirate",         "0");
					await writer.WriteAttributeAsync("physical",       "0");
					await writer.WriteAttributeAsync("complete",       "1");
					await writer.WriteAttributeAsync("nodump",         "0");
					await writer.WriteAttributeAsync("public",         "1");
					await writer.WriteAttributeAsync("dat",            "1");
					await writer.WriteEndElementAsync();
					writer.WriteStartElement("source");
					writer.WriteStartElement("details");
					await writer.WriteAttributeAsync("name",           "Trusted Dump");
					await writer.WriteAttributeAsync("rominfo",        "");
					await writer.WriteAttributeAsync("date",           File.GetLastWriteTime(Path.Combine(titleDir, metadataFiles[newestMetadataIndex].fileName)).ToString("yyyy-MM-dd"));
					await writer.WriteAttributeAsync("known",          "1");
					await writer.WriteAttributeAsync("dumper",         "");
					await writer.WriteAttributeAsync("project",        "");
					await writer.WriteAttributeAsync("session",        "");
					await writer.WriteAttributeAsync("tool",           "NusRipper v" + Constants.ProgramVersion);
					await writer.WriteAttributeAsync("origin",         "CDN");
					await writer.WriteAttributeAsync("comment1",       "");
					await writer.WriteAttributeAsync("comment2",       "");
					await writer.WriteAttributeAsync("link1",          "");
					await writer.WriteAttributeAsync("link2",          "");
					await writer.WriteAttributeAsync("region",         "");
					await writer.WriteAttributeAsync("mediatitle",     "");
					await writer.WriteEndElementAsync();
					writer.WriteStartElement("serials");
					await writer.WriteAttributeAsync("mediaserial1",   "");
					await writer.WriteAttributeAsync("mediaserial2",   "");
					await writer.WriteAttributeAsync("pcbserial",      "");
					await writer.WriteAttributeAsync("romchipserial1", "");
					await writer.WriteAttributeAsync("romchipserial2", "");
					await writer.WriteAttributeAsync("lockoutserial",  "");
					await writer.WriteAttributeAsync("savechipserial", "");
					await writer.WriteAttributeAsync("chipserial",     "");
					await writer.WriteAttributeAsync("boxserial",      "");
					await writer.WriteAttributeAsync("mediastamp",     "");
					await writer.WriteAttributeAsync("boxbarcode",     "");
					await writer.WriteAttributeAsync("digitalserial1", titleIdLower);
					await writer.WriteAttributeAsync("digitalserial2", titleIdGameCode);
					await writer.WriteEndElementAsync();

					foreach (string ticketFile in ticketFiles)
						await WriteCdnRomEntry(writer, titleDir, ticketFile, duplicateDecVersion: true);

					foreach ((string fileName, TitleMetadata metadata) metadata in metadataFiles)
						await WriteCdnRomEntry(writer, titleDir, metadata.fileName, metadata.metadata.TitleVersion.ToString(), duplicateDecVersion: true);

					foreach (KeyValuePair<string, (string fileName, RomInfo info, int metadataIndex)> contentFileEntry in contentDecryptedFiles)
					{
						List<string> serialParts = new List<string>();
						if (!string.IsNullOrEmpty(contentFileEntry.Value.info.GameCode))
							serialParts.Add(contentFileEntry.Value.info.GameCode);
						//else
						//	serialParts.Add(RomInfo.DeriveGameCodeFromTitleId(titleId));
						if (!string.IsNullOrEmpty(contentFileEntry.Value.info.GameTitle?.Trim()))
							serialParts.Add(contentFileEntry.Value.info.GameTitle.Trim());
						string serialString = string.Join(", ", serialParts);

						string versionString = metadataFiles[contentFileEntry.Value.metadataIndex].metadata.TitleVersion.ToString();
						await WriteCdnRomEntry(writer,
							titleDir,
							contentFileEntry.Key,
							versionString,
							serialString);
						await WriteDecRomEntry(writer,
							titleDir,
							contentFileEntry.Value.fileName,
							versionString,
							serialString,
							contentFileEntry.Key);
					}

					await writer.WriteEndElementAsync();
					await writer.WriteEndElementAsync();
				}
				await writer.WriteEndElementAsync();
			}

			Log.Instance.Info($"Completed the batch processing in {stopwatch.Elapsed.ToNiceString()}.");
		}

		private static (Hasher.FileHashCollection hashes, DateTime? modTime) GetFileMetaInfo(string filePath)
		{
			DateTime? modTime = null;
			if (File.Exists(filePath + Ripper.HeaderFileSuffix))
				foreach (string line in File.ReadAllLines(filePath + Ripper.HeaderFileSuffix))
				{
					int colonIndex = line.IndexOf(':');
					if (colonIndex < 0 || line.Substring(0, colonIndex).Trim() != LastModifiedLabel)
						continue;
					modTime = DateTime.ParseExact(line.Substring(colonIndex + 1).Trim(),
						"ddd, dd MMM yyyy HH:mm:ss 'GMT'",
						CultureInfo.InvariantCulture.DateTimeFormat,
						DateTimeStyles.AssumeUniversal);
					break;
				}

			Hasher.FileHashCollection hashes = Hasher.FileHashCollection.ReadFromFile(filePath + Ripper.HashesFileSuffix, filePath);

			if(modTime == null)
				Log.Instance.Error($"Unable to get the upload date from the file '{filePath + Ripper.HeaderFileSuffix}'.");

			return (hashes, modTime);
		}

		private static async Task WriteCdnRomEntry(XmlWriter writer, string titleDir, string fileName, string versionStr = "", string serialStr = "", bool duplicateDecVersion = false)
		{
			(Hasher.FileHashCollection hashes, DateTime? modTime) = GetFileMetaInfo(Path.Combine(titleDir, fileName));

			writer.WriteStartElement("rom");
			await writer.WriteAttributeAsync("dirname",   "");
			await writer.WriteAttributeAsync("forcename", fileName);
			await writer.WriteAttributeAsync("extension", "");
			await writer.WriteAttributeAsync("item",      "");
			await writer.WriteAttributeAsync("date",      modTime?.ToString("yyyy-MM-dd"));
			await writer.WriteAttributeAsync("format",    "CDN");
			await writer.WriteAttributeAsync("version",   versionStr);
			await writer.WriteAttributeAsync("utype",     "");
			await writer.WriteAttributeAsync("size",      hashes.FileSize.ToString());
			await writer.WriteAttributeAsync("crc",       hashes.Crc32String);
			await writer.WriteAttributeAsync("md5",       hashes.Md5String);
			await writer.WriteAttributeAsync("sha1",      hashes.Sha1String);
			await writer.WriteAttributeAsync("sha256",    hashes.Sha256String);
			await writer.WriteAttributeAsync("serial",    serialStr);
			await writer.WriteAttributeAsync("bad",       "0");
			await writer.WriteEndElementAsync();

			if (duplicateDecVersion)
				await WriteDecRomEntry(writer, fileName, hashes, modTime, versionStr, serialStr);
		}

		private static async Task WriteDecRomEntry(XmlWriter writer, string fileName, Hasher.FileHashCollection hashes, DateTime? modTime, string versionStr = "", string serialStr = "", string displayFileName = null)
		{
			displayFileName ??= fileName;

			writer.WriteStartElement("rom");
			await writer.WriteAttributeAsync("dirname",   "");
			await writer.WriteAttributeAsync("forcename", displayFileName);
			await writer.WriteAttributeAsync("extension", "");
			await writer.WriteAttributeAsync("item",      "");
			await writer.WriteAttributeAsync("date",      modTime?.ToString("yyyy-MM-dd"));
			await writer.WriteAttributeAsync("format",    "CDNdec");
			await writer.WriteAttributeAsync("version",   versionStr);
			await writer.WriteAttributeAsync("utype",     "");
			await writer.WriteAttributeAsync("size",      hashes.FileSize.ToString());
			await writer.WriteAttributeAsync("crc",       hashes.Crc32String);
			await writer.WriteAttributeAsync("md5",       hashes.Md5String);
			await writer.WriteAttributeAsync("sha1",      hashes.Sha1String);
			await writer.WriteAttributeAsync("sha256",    hashes.Sha256String);
			await writer.WriteAttributeAsync("serial",    serialStr);
			await writer.WriteAttributeAsync("bad",       "0");
			await writer.WriteEndElementAsync();
		}

		private static async Task WriteDecRomEntry(XmlWriter writer, string titleDir, string fileName, string versionStr = "", string serialStr = "", string displayFileName = null)
		{
			Hasher.FileHashCollection hashes = new Hasher.FileHashCollection(Path.Combine(titleDir, fileName));
			DateTime modTime = File.GetLastWriteTime(Path.Combine(titleDir, fileName));

			await WriteDecRomEntry(writer, fileName, hashes, modTime, versionStr, serialStr, displayFileName);
		}
	}
}
