using Romanization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using NTextCat.Commons;

namespace NusRipper
{
	internal static class DatOMatic
	{
		private const string LastModifiedLabel = "Last-Modified";
		private const string RemoveNoIntroDisallowedChars = @"[^a-zA-Z0-9 $!#%'()+,\-.;=@\[\]\^_{}~]";
		private const string DeletedTitleFileName = "DeletedTitle.txt"; // The file name of a file used to indicate a title had been deleted from the DSi Shop
		private const int StartingArchiveId = 1; // The dat should be the establishing set, so it can start at 0001

		// I've tried really hard to keep the functions of this tool generic and largely system-agnostic, but unfortunately I had to do it this way
		// ReSharper disable twice IdentifierTypo
		// ReSharper disable twice InconsistentNaming
		private enum DumperNames
		{
			zedseven,
			Galaxy,
			Larsenv
		}
		private static readonly DumperNames MaxDumper = Enum.GetValues(typeof(DumperNames)).Cast<DumperNames>().Max();

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
		private static Regions.Region RegionFromCharCode(char charCode)
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

		// The order in which regions should be sorted when attempting to determine the parent (master copy) of a title
		private static readonly char[] CharCodeRegionOrder = 
		{
			'A', // World
			'V', // Europe, Australia
			'P', // Europe
			'O', // USA, Europe
			'T', // USA, Australia
			'E', // USA
			'U', // Australia
			'X', // Europe
			'F', // France
			'D', // Germany
			'S', // Spain
			'I', // Italy
			'H', // Netherlands
			'J', // Japan
			'K', // Korea
			'C'  // China
		};

		[Serializable]
		private sealed class FullTitleInfo
		{
			// Title Metadata
			public string TitleId;
			public string TitleIdLower => TitleId.ToLowerInvariant();
			public string TitleIdGameCode;
			public char RegionCode;
			public Regions.Region Region;
			public string NoIntroTitle;
			public bool System;
			public bool Deleted;
			public HashSet<Languages.LanguageCode> Languages = new HashSet<Languages.LanguageCode>();
			public List<Languages.LanguageCode> NebulousLanguages = new List<Languages.LanguageCode>();
			public RomInfo.TitleIndices PrimaryLanguageIndex;
			public bool Found3dsPort;

			// Title File Info
			public List<string> TicketFiles = new List<string>();
			public List<(string fileName, TitleMetadata metadata)> MetadataFiles = new List<(string fileName, TitleMetadata metadata)>();
			public List<string> ContentEncryptedFiles = new List<string>();
			public Dictionary<string, (string fileName, RomInfo info, int metadataIndex)> ContentDecryptedFiles = new Dictionary<string, (string fileName, RomInfo info, int metadataIndex)>();

			// Working Variables
			public string TitleDir;
			public int NewestMetadataIndex;
			public string NewestContentId;
		}

		internal static async Task BuildXmlFromFiles(string archiveDir, string outputXmlPath, string outputMediaPath = null, bool makeNdsFiles = false)
		{
			Stopwatch mainStopwatch = Stopwatch.StartNew();
			Stopwatch infoStopwatch = Stopwatch.StartNew();

			bool writeMedia = outputMediaPath != null;

			Log.Instance.Info(makeNdsFiles
				? $"Beginning a build of a DAT-o-MATIC XML dat file of '{archiveDir}', writing to '{outputXmlPath}' and '{outputMediaPath}', as well as making NDS files."
				: writeMedia
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

			// Run through all titles and collect their information
			List<FullTitleInfo> allTitleInfo = new List<FullTitleInfo>();
			foreach (string titleDir in Directory.EnumerateDirectories(archiveDir))
			{
				// Create the data container
				FullTitleInfo titleInfo = new FullTitleInfo
				{
					TitleDir = titleDir
				};

				// Manage basic Title ID stuff
				titleInfo.TitleId = Path.GetFileName(titleDir);
				titleInfo.TitleIdGameCode = RomInfo.DeriveGameCodeFromTitleId(titleInfo.TitleId);

				Log.Instance.Info($"{titleInfo.TitleId} - {titleInfo.TitleIdGameCode}");

				// Parse out the files in the dir into manageable entries
				foreach (string filePath in Directory.EnumerateFiles(titleDir))
				{
					string fileName = Path.GetFileName(filePath);
					if (fileName == DeletedTitleFileName)
					{
						titleInfo.Deleted = true;
						continue;
					}
					if (fileName == Ripper.TicketFileName)
					{
						titleInfo.TicketFiles.Add(fileName);
						continue;
					}
					if (Constants.MetadataFileRegex.IsMatch(fileName))
					{
						titleInfo.MetadataFiles.Add((fileName, new TitleMetadata(filePath)));
						continue;
					}
					if (Constants.ContentEncryptedFileRegex.IsMatch(fileName))
					{
						titleInfo.ContentEncryptedFiles.Add(fileName);
						continue;
					}
					if (Constants.ContentDecryptedFileRegex.IsMatch(fileName))
					{
						titleInfo.ContentDecryptedFiles.Add(fileName.Split('.')[0], (fileName, new RomInfo(filePath), -1));
						continue;
					}
				}

				// Check for empty/malformed folders
				int totalCheckFiles = titleInfo.MetadataFiles.Count + titleInfo.ContentEncryptedFiles.Count + titleInfo.ContentDecryptedFiles.Count;
				if (totalCheckFiles < 3)
				{
					Log.Instance.Error($"Dir '{titleDir}' has below the expected number of files: {totalCheckFiles}. Skipping...");
					continue;
				}

				// Sort metadata files by their extension, since with alphanumeric ordering tmd.1024 comes before tmd.256 and it looks messy
				titleInfo.MetadataFiles.Sort((m, n) =>
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
				titleInfo.NewestMetadataIndex = -1;
				{
					int newestMetadataVersion = -1;
					for (int i = titleInfo.MetadataFiles.Count - 1; i >= 0; i--)
					{
						if (titleInfo.MetadataFiles[i].metadata.TitleVersion > newestMetadataVersion)
						{
							newestMetadataVersion = titleInfo.MetadataFiles[i].metadata.TitleVersion;
							titleInfo.NewestMetadataIndex = i;
						}
						for (int j = 0; j < titleInfo.MetadataFiles[i].metadata.ContentInfo.Length; j++)
						{
							string contentName = titleInfo.MetadataFiles[i].metadata.ContentInfo[j].Id.ToString("x8");
							if (titleInfo.ContentDecryptedFiles.ContainsKey(contentName))
								titleInfo.ContentDecryptedFiles[contentName] = (
									titleInfo.ContentDecryptedFiles[contentName].fileName,
									titleInfo.ContentDecryptedFiles[contentName].info,
									i);
							else
								Log.Instance.Error($"The content file '{contentName}' does not exist, but should according to the metadata file '{titleInfo.MetadataFiles[i].fileName}'.");
						}
					}
				}
				titleInfo.NewestContentId = titleInfo.MetadataFiles[titleInfo.NewestMetadataIndex].metadata.ContentInfo[0].Id.ToString("x8");

				// Ensure the number of encrypted titles matches the number of decrypted ones (more of a sanity check than anything)
				if (titleInfo.ContentEncryptedFiles.Count != titleInfo.ContentDecryptedFiles.Count)
					Log.Instance.Error($"The number of encrypted files ({titleInfo.ContentEncryptedFiles.Count}) is mismatched with the number of decrypted ones ({titleInfo.ContentDecryptedFiles.Count}).");

				// From here until the XML writing, it's mostly about the most recent content

				// Region
				titleInfo.RegionCode = titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].info.RegionCode;
				if (titleInfo.RegionCode == '\0')
				{
					Log.Instance.Warn($"The ROM belonging to title ID '{titleInfo.TitleId}' of the filename '{titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].fileName}' " +
									  "is not valid, and the gamecode for the title will be derived from the title ID.");
					titleInfo.RegionCode = titleInfo.TitleIdGameCode[RomInfo.GameCodeLength - 1];
				}
				titleInfo.Region = RegionFromCharCode(titleInfo.RegionCode);
				Regions.Region primaryRegion = titleInfo.Region.GetPrimaryRegion();

				// Languages
				string primaryTitle = null;
				titleInfo.Languages = new HashSet<Languages.LanguageCode>();
				titleInfo.NebulousLanguages = new List<Languages.LanguageCode>();

				// If the DSiWare title was ported to 3DS, attempt to find it on the eShop to get language information
				// This is basically just going through the various region 'storefronts' for the title - all should have
				// the same information, just in different languages, though only valid regions for the game have anything at all
				Languages.LanguageCode[] title3dsLanguages = null;
				foreach (Regions.Region reg in titleInfo.Region
					.DecomposeRegion()                                                        // Break apart things like "Europe, Australia" into their individual regions
					.Reverse()                                                                // Reverse because often regions are "Europe, ..." and greater specificity is preferred
					.SelectMany(r => new [] { r }.Concat(Regions.RegionRelatedRegionsMap[r])) // Get all related regions
					.Distinct())                                                              // No duplicates
				{
					if (Regions.RegionTwoLetterCodes.TryGetValue(reg, out string twoLetter))
						title3dsLanguages = await Port3dsInfo.GetTitle3dsInfo(titleInfo.TitleId, twoLetter);
					if (title3dsLanguages != null)
						break;
				}

				titleInfo.Found3dsPort = title3dsLanguages != null;
				Languages.LanguageCode[] regionLangs = Regions.GetExpectedLanguages(titleInfo.Region);
				if (titleInfo.Found3dsPort)
				{
					Log.Instance.Debug($"Title '{titleInfo.TitleId}' has an existing 3DS port, with languages \"{string.Join(", ", title3dsLanguages.Select(l => l.Code))}\".");
					title3dsLanguages.ForEach(l => titleInfo.Languages.Add(l));
				}
				if (titleInfo.Languages.Count <= 0)
				{
					if (title3dsLanguages == null && Port3dsInfo.Has3dsPort(titleInfo.TitleId))
						Log.Instance.Warn($"Title '{titleInfo.TitleId}' is supposed to have a 3DS port, but nothing was found. Deriving language and title information from the latest ROM.");
					else
						Log.Instance.Debug($"Title '{titleInfo.TitleId}' does not have an existing 3DS port or the supported languages were not able to be found. Deriving language and title information from the latest ROM.");

					Tuple<string, int, HashSet<Languages.LanguageCode>>[] titleGroups = titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].info.Titles
						.Select((t, i) => (t: Languages.SanitizeRomTitleForLanguageDetermination(t), i))
						.GroupBy(t => t.t)
						.Select(e => new Tuple<string, int, HashSet<Languages.LanguageCode>>(
							e.Key,
							e.First().i,
							e.Select(v => RomInfo.TitleIndexToLanguageCode(v.i))
								.ToHashSet()))
						.ToArray();

					foreach (Tuple<string, int, HashSet<Languages.LanguageCode>> titleEntry in titleGroups.Where(
						g => g.Item3.Count == 1))
						titleInfo.Languages.Add(RomInfo.TitleIndexToLanguageCode(titleEntry.Item2));

					Tuple<string, int, HashSet<Languages.LanguageCode>>[] titleConflicts = titleGroups
						.Where(g => g.Item3.Count > 1)
						.ToArray();
					for (int i = 0; i < titleConflicts.Length; i++)
					{
						if (string.IsNullOrWhiteSpace(titleConflicts[i].Item1))
							continue;
						string sanitizedTitle = titleConflicts[i].Item1;

						Log.Instance.Trace(
							$"Title '{titleInfo.TitleId}' has a title conflict for title '{sanitizedTitle}' ({titleConflicts[i].Item3} titles are the same).");

						List<Languages.LanguageCode> langs = Languages.DetermineLanguage(sanitizedTitle);
						langs = langs
							.OrderBy(l => titleConflicts[i].Item3.Contains(l) ? 0 : 1)
							.ThenBy(l => regionLangs.Contains(l) ? 0 : 1)
							.ToList();

						bool addedLang = false;
						for (int j = 0; j < langs.Count; j++)
						{
							if (titleInfo.Languages.Contains(langs[j]))
								continue;
							titleInfo.Languages.Add(langs[j]);
							titleInfo.NebulousLanguages.Add(langs[j]);
							addedLang = true;
							if (j > 0)
								Log.Instance.Debug(
									"The first choice of language was not chosen, as it was already defined by a known language instance. " +
									$"Language choice {j} (0-indexed) was chosen - {langs[j]}. The title in question: '{sanitizedTitle}'");
							break;
						}

						if (!addedLang)
						{
							Languages.LanguageCode defaultLang = Regions.RegionExpectedLanguageMap[primaryRegion][0];
							Log.Instance.Warn(
								$"No languages were found or determined for the title '{titleInfo.TitleId}'s conflict \"{titleConflicts[i]}\". Proceeding with the default language for the region ({titleInfo.Region}), {defaultLang}.");
							titleInfo.Languages.Add(defaultLang);
							titleInfo.NebulousLanguages.Add(defaultLang);
						}
					}

					if (titleInfo.Languages.Count <= 0)
					{
						Languages.LanguageCode defaultLang = Regions.RegionExpectedLanguageMap[primaryRegion][0];
						Log.Instance.Warn(
							$"No languages were found or determined for the title '{titleInfo.TitleId}'. Proceeding with the default language for the region ({titleInfo.Region}), {defaultLang}.");
						titleInfo.Languages.Add(defaultLang);
						titleInfo.NebulousLanguages.Add(defaultLang);
					}
				}

				// No-Intro Title
				Languages.LanguageCode primaryLanguage = titleInfo.Languages.OrderBy(x =>
				{
					int index = Array.IndexOf(regionLangs, x);
					return index > -1 ? index : int.MaxValue;
				}).First();
				titleInfo.PrimaryLanguageIndex = RomInfo.LanguageCodeToTitleIndex(primaryLanguage);
				primaryTitle ??= titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].info.Titles[(int) titleInfo.PrimaryLanguageIndex];
				string[] primaryTitleParts = primaryTitle?.Split('\n');
				titleInfo.NoIntroTitle = "";
				if (primaryTitle != null && primaryTitleParts.Length > 0)
					titleInfo.NoIntroTitle = GetNoIntroTitle(primaryTitleParts[0], primaryTitleParts.Length > 2 ? primaryTitleParts[1] : null, primaryLanguage);

				titleInfo.System = titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].info.GetTitleAsNullIfDefault(titleInfo.PrimaryLanguageIndex) == null ||
				                   titleInfo.TitleIdGameCode.ToUpperInvariant()[0] == RomInfo.SystemTitleGameCodeChar;

				// Add the title info to the collection
				allTitleInfo.Add(titleInfo);
			}

			Log.Instance.Info($"Completed the info and file processing in {infoStopwatch.ElapsedAfterStopped().ToNiceString()}.");

			// Cache the results for later so that calculation can be skipped in development
			await using (FileStream sfs = new FileStream("titleInfo.dat", FileMode.Create))
			{
				BinaryFormatter formatter = new BinaryFormatter();
				try
				{
					formatter.Serialize(sfs, allTitleInfo);
				}
				catch (SerializationException e)
				{
					Log.Instance.Error($"Unable to serialize the title info. Reason: {e.Message}");
				}
			}

			Stopwatch datStopwatch = Stopwatch.StartNew();
			Log.Instance.Info("Beginning the dat file creation.");

			await using (FileStream xfs = new FileStream(outputXmlPath, FileMode.Create))
			using (XmlWriter xWriter = XmlWriter.Create(xfs, settings))
			await using (FileStream mfs = writeMedia ? new FileStream(outputMediaPath, FileMode.Create) : null)
			await using (StreamWriter mWriter = writeMedia ? new StreamWriter(mfs, Encoding.UTF8) : null)
			await using (FileStream tfs = writeMedia ? new FileStream(Path.Combine(Path.GetDirectoryName(outputMediaPath), "titles.csv"), FileMode.Create) : null)
			await using (StreamWriter tWriter = writeMedia ? new StreamWriter(tfs, Encoding.UTF8) : null)
			{
				await xWriter.WriteStartDocumentAsync();

				await xWriter.WriteRawAsync("\r\n<!DOCTYPE datafile PUBLIC \"http://www.logiqx.com/Dats/datafile.dtd\" \"-//Logiqx//DTD ROM Management Datafile//EN\">\r\n");

				xWriter.WriteStartElement("datafile");
				xWriter.WriteStartElement("header");
				await xWriter.WriteEndElementAsync();

				if (writeMedia)
				{
					await WriteCsvRow(mWriter, "Title ID", "Region", "Language Determination Method",
						"Nebulously-Determined Languages", "Only Title",
						"English", "Japanese", "French", "German", "Spanish", "Italian", "Chinese", "Korean");
					await WriteCsvRow(tWriter, "Title ID", "No-Intro Title");
				}

				IEnumerable<(string regionAgnosticKey, List<FullTitleInfo> titles)> preparedTitleInfo = allTitleInfo
					.GroupBy(t => t.TitleId.Substring(0, 14))                          // Collect titles into region-agnostic groups (only the region code (last 2 chars) changes for regional releases of a title)
					.Select(g => (regionAgnosticKey: g.Key, titles: g
						.OrderBy(t => t.Languages.Contains(Languages.English) ? 0 : 1) // Releases containing English are preferred as the parent copy, as No-Intro seems to be Europe-biased
						.ThenBy(t => t.Languages.Count)                                // Releases with the most supported languages are preferred
						.ThenBy(t =>
						{
							int index = Array.IndexOf(CharCodeRegionOrder, t.RegionCode);
							return index > -1 ? index : int.MaxValue;
						})                                                             // If a tie still hasn't been broken, choose based on region code, with preference for greater coverage by the region
						.ToList()));

				int archiveId = StartingArchiveId;
				foreach ((string regionAgnosticKey, List<FullTitleInfo> titles) titleGroup in preparedTitleInfo)
				{
					int parentArchiveId = archiveId;
					for (int i = 0; i < titleGroup.titles.Count; i++)
					{
						FullTitleInfo titleInfo = titleGroup.titles[i];

						string xmlName = $"{archiveId.ToString().PadLeft(4, '0')} - {titleInfo.NoIntroTitle}";

						// Write the XML
						xWriter.WriteStartElement("game");
						await xWriter.WriteAttributeAsync("name", xmlName);
						xWriter.WriteStartElement("archive");
						await xWriter.WriteAttributeAsync("name",           xmlName);
						await xWriter.WriteAttributeAsync("namealt",        "");
						await xWriter.WriteAttributeAsync("region",         titleInfo.Region.ToString());
						await xWriter.WriteAttributeAsync("languages",
							string.Join(',', titleInfo.Languages.OrderBy(l =>
							{
								int index = LanguageOrder.IndexOf(l);
								return index > -1 ? index : int.MaxValue;
							}).Select(l => l.Code)));
						await xWriter.WriteAttributeAsync("version",        "");
						await xWriter.WriteAttributeAsync("devstatus",      "");
						await xWriter.WriteAttributeAsync("additional",     "");
						await xWriter.WriteAttributeAsync("special1",       titleInfo.System  ? "System"  : "");
						await xWriter.WriteAttributeAsync("special2",       titleInfo.Deleted ? "Removed" : "");
						await xWriter.WriteAttributeAsync("gameid",         "");
						await xWriter.WriteAttributeAsync("clone",
							i == 0 && titleGroup.titles.Count > 1 ? "P" :
							i > 0 ? parentArchiveId.ToString().PadLeft(4, '0') : "");
						await xWriter.WriteAttributeAsync("regionalparent", "");
						await xWriter.WriteEndElementAsync();
						xWriter.WriteStartElement("flags");
						await xWriter.WriteAttributeAsync("bios",           "0");
						await xWriter.WriteAttributeAsync("licensed",       "1");
						await xWriter.WriteAttributeAsync("pirate",         "0");
						await xWriter.WriteAttributeAsync("physical",       "0");
						await xWriter.WriteAttributeAsync("complete",       "1");
						await xWriter.WriteAttributeAsync("nodump",         "0");
						await xWriter.WriteAttributeAsync("public",         "1");
						await xWriter.WriteAttributeAsync("dat",            "1");
						await xWriter.WriteEndElementAsync();

						// Source entries for each dumper involved in the project, to capture their work and give them appropriate credit
						// it's a bit of a mess, but this was the cleanest way to do it without an exorbitant amount of extra work
						for (DumperNames dumperName = 0; dumperName <= MaxDumper; dumperName++)
						{
							string dumpDate = dumperName switch
							{
								DumperNames.zedseven => File.GetLastWriteTime(Path.Combine(titleInfo.TitleDir, titleInfo.MetadataFiles[titleInfo.NewestMetadataIndex].fileName)).ToString("yyyy-MM-dd"),
								DumperNames.Galaxy => "",
								DumperNames.Larsenv => "2018-11-17"
							};

							// TODO: Calc date for Galaxy
							xWriter.WriteStartElement("source");
							xWriter.WriteStartElement("details");
							await xWriter.WriteAttributeAsync("section",          "Trusted Dump");
							await xWriter.WriteAttributeAsync("rominfo",          "");
							await xWriter.WriteAttributeAsync("dumpdate",         dumpDate);
							await xWriter.WriteAttributeAsync("releasedate",      dumpDate);
							await xWriter.WriteAttributeAsync("knownreleasedate", "0");
							await xWriter.WriteAttributeAsync("known",            dumperName != DumperNames.Larsenv ? "1" : "0");
							await xWriter.WriteAttributeAsync("dumper",           dumperName.ToString());
							await xWriter.WriteAttributeAsync("project",          "");
							await xWriter.WriteAttributeAsync("session",          "");
							await xWriter.WriteAttributeAsync("tool",             dumperName == DumperNames.zedseven ? "NusRipper v" + Constants.ProgramVersion : "Custom");
							await xWriter.WriteAttributeAsync("origin",           "CDN");
							await xWriter.WriteAttributeAsync("comment1",         "");
							await xWriter.WriteAttributeAsync("comment2",         "");
							await xWriter.WriteAttributeAsync("link1",            "");
							await xWriter.WriteAttributeAsync("link2",            "");
							await xWriter.WriteAttributeAsync("region",           "");
							await xWriter.WriteAttributeAsync("mediatitle",       "");
							await xWriter.WriteEndElementAsync();
							xWriter.WriteStartElement("serials");
							await xWriter.WriteAttributeAsync("mediaserial1",     "");
							await xWriter.WriteAttributeAsync("mediaserial2",     "");
							await xWriter.WriteAttributeAsync("pcbserial",        "");
							await xWriter.WriteAttributeAsync("romchipserial1",   "");
							await xWriter.WriteAttributeAsync("romchipserial2",   "");
							await xWriter.WriteAttributeAsync("lockoutserial",    "");
							await xWriter.WriteAttributeAsync("savechipserial",   "");
							await xWriter.WriteAttributeAsync("chipserial",       "");
							await xWriter.WriteAttributeAsync("boxserial",        "");
							await xWriter.WriteAttributeAsync("mediastamp",       "");
							await xWriter.WriteAttributeAsync("boxbarcode",       "");
							await xWriter.WriteAttributeAsync("digitalserial1",   titleInfo.TitleIdLower);
							await xWriter.WriteAttributeAsync("digitalserial2",   titleInfo.TitleIdGameCode);
							await xWriter.WriteEndElementAsync();

							if (!titleInfo.Deleted || dumperName == DumperNames.Galaxy)
							{
								foreach (string ticketFile in titleInfo.TicketFiles)
									await WriteCdnRomEntry(xWriter, titleInfo.TitleDir, ticketFile,
										duplicateDecVersion: dumperName == DumperNames.zedseven);

								foreach ((string fileName, TitleMetadata metadata) metadata in titleInfo.MetadataFiles)
									await WriteCdnRomEntry(xWriter, titleInfo.TitleDir, metadata.fileName,
										metadata.metadata.TitleVersion.ToString(),
										duplicateDecVersion: dumperName == DumperNames.zedseven);
							}

							foreach (KeyValuePair<string, (string fileName, RomInfo info, int metadataIndex)> contentFileEntry in titleInfo.ContentDecryptedFiles)
							{
								List<string> serialParts = new List<string>();
								if (!string.IsNullOrEmpty(contentFileEntry.Value.info.GameCode))
									serialParts.Add(contentFileEntry.Value.info.GameCode);
								//else
								//	serialParts.Add(RomInfo.DeriveGameCodeFromTitleId(titleId));
								if (!string.IsNullOrEmpty(contentFileEntry.Value.info.GameTitle?.Trim()))
									serialParts.Add(contentFileEntry.Value.info.GameTitle.Trim());
								string serialString = string.Join(", ", serialParts);

								string versionString = titleInfo.MetadataFiles[contentFileEntry.Value.metadataIndex].metadata.TitleVersion.ToString();
								if (!titleInfo.Deleted || dumperName == DumperNames.Galaxy)
									await WriteCdnRomEntry(xWriter,
										titleInfo.TitleDir,
										contentFileEntry.Key,
										versionString,
										serialString);
								if (dumperName == DumperNames.zedseven)
									await WriteDecRomEntry(xWriter,
										titleInfo.TitleDir,
										contentFileEntry.Value.fileName,
										versionString,
										serialString,
										contentFileEntry.Key);
							}

							await xWriter.WriteEndElementAsync();
						}

						await xWriter.WriteEndElementAsync();

						// Write the row for the Media CSV
						if (writeMedia)
						{
							await WriteCsvRow(mWriter,
								titleInfo.TitleId,
								titleInfo.Region.ToString(),
								titleInfo.Found3dsPort ? "3DS eShop Port" : "Guessed From ROM Titles",
								string.Join(',', titleInfo.NebulousLanguages.Select(l => l.Code)),
								titleInfo.Languages.Count == 1                   ? titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].info.GetTitleOnly(titleInfo.PrimaryLanguageIndex)               : "",
								titleInfo.Languages.Contains(Languages.English)  ? titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].info.GetTitleOnly(RomInfo.TitleIndices.EnglishIndex)  : "",
								titleInfo.Languages.Contains(Languages.Japanese) ? titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].info.GetTitleOnly(RomInfo.TitleIndices.JapaneseIndex) : "",
								titleInfo.Languages.Contains(Languages.French)   ? titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].info.GetTitleOnly(RomInfo.TitleIndices.FrenchIndex)   : "",
								titleInfo.Languages.Contains(Languages.German)   ? titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].info.GetTitleOnly(RomInfo.TitleIndices.GermanIndex)   : "",
								titleInfo.Languages.Contains(Languages.Spanish)  ? titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].info.GetTitleOnly(RomInfo.TitleIndices.SpanishIndex)  : "",
								titleInfo.Languages.Contains(Languages.Italian)  ? titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].info.GetTitleOnly(RomInfo.TitleIndices.ItalianIndex)  : "",
								titleInfo.Languages.Contains(Languages.Chinese)  ? titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].info.GetTitleOnly(RomInfo.TitleIndices.ChineseIndex)  : "",
								titleInfo.Languages.Contains(Languages.Korean)   ? titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].info.GetTitleOnly(RomInfo.TitleIndices.KoreanIndex)   : "");
							await WriteCsvRow(tWriter, titleInfo.TitleId, titleInfo.NoIntroTitle);
						}

						// Make user-friendly NDS files of the latest version of each title
						if (makeNdsFiles)
							File.Copy(Path.Combine(titleInfo.TitleDir, titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].fileName),
								Path.Combine(titleInfo.TitleDir, $"{(titleInfo.System ? "[BIOS] " : "")}{titleInfo.NoIntroTitle} ({titleInfo.Region.ToString()}).nds"),
								true);

						archiveId++;
					}
				}
				await xWriter.WriteEndElementAsync();
			}

			Log.Instance.Info($"Completed the dat file creation in {datStopwatch.ElapsedAfterStopped().ToNiceString()}.");
			Log.Instance.Info($"Completed the batch processing in {mainStopwatch.ElapsedAfterStopped().ToNiceString()}.");
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

		/// <summary>
		/// Converts a title to a title matching the official No-Intro convention, as defined in:
		/// <a href='https://datomatic.no-intro.org/stuff/The%20Official%20No-Intro%20Convention%20(20071030).pdf'>https://datomatic.no-intro.org/stuff/The%20Official%20No-Intro%20Convention%20(20071030).pdf</a>
		/// </summary>
		/// <param name="title">The title to convert.</param>
		/// <param name="subTitle"></param>
		/// <param name="language"></param>
		/// <returns></returns>
		[Pure]
		public static string GetNoIntroTitle(string title, string subTitle, Languages.LanguageCode language)
		{
			StringBuilder retTitle = new StringBuilder();
			bool doSubTitle = !string.IsNullOrWhiteSpace(subTitle);

			// Romanize title if necessary
			if (language == Languages.Japanese)
			{
				title = Japanese.KanjiReadings.Value.ProcessWithKana(title, Japanese.ModifiedHepburn.Value);
				if (doSubTitle)
					subTitle = Japanese.KanjiReadings.Value.ProcessWithKana(subTitle, Japanese.ModifiedHepburn.Value);
			}
			else if (language == Languages.Chinese)
			{
				title = Chinese.HanyuPinyin.Value.Process(title);
				if (doSubTitle)
					subTitle = Chinese.HanyuPinyin.Value.Process(subTitle);
			}
			else if (language == Languages.Korean)
			{
				title = Korean.HanjaReadings.Value.Process(title, Korean.RevisedRomanization.Value);
				if (doSubTitle)
					subTitle = Korean.HanjaReadings.Value.Process(subTitle, Korean.RevisedRomanization.Value);
			}

			title = Regex.Replace(Regex.Replace(title,
							Languages.RemoveTrademarkReminders, "") // Remove Trademark Reminders
						.FoldToAscii(),                             // Convert to Low ASCII
					RemoveNoIntroDisallowedChars, "")               // Remove additional Low ASCII characters that are not allowed
				.Trim(' ', ':', '-')                                // Remove sub title separation characters and spaces
				.ToTitleCase();                                     // Convert to Title Case

			// Move common articles at the beginning of the title to the end (ie. "The Legend of Zelda" to "Legend of Zelda, The")
			foreach (string commonArticle in Languages.CommonArticles)
			{
				if (!title.StartsWith(commonArticle + " ", StringComparison.InvariantCultureIgnoreCase))
					continue;

				title = $"{title.Substring(commonArticle.Length + 1)}, {title.Substring(0, commonArticle.Length)}";
				break;
			}

			retTitle.Append(title);

			if (doSubTitle)
			{
				subTitle = Regex.Replace(Regex.Replace(subTitle,
								Languages.RemoveTrademarkReminders, "") // Remove Trademark Reminders
							.FoldToAscii(),                             // Convert to Low ASCII
						RemoveNoIntroDisallowedChars, "")               // Remove additional Low ASCII characters that are not allowed
					.Trim(' ', ':', '-')                                // Remove sub title separation characters and spaces
					.ToTitleCase();                                     // Convert to Title Case

				retTitle.Append($" - {subTitle}");
			}

			return Regex.Replace(retTitle.ToString(), @"\s+", " ") // Remove duplicate whitespace
				.Trim(' ', '.');                                   // Trim off disallowed start/end characters
		}

		private static async Task WriteCsvRow(TextWriter writer, params string[] values)
			=> await writer.WriteLineAsync(string.Join(',',
				values.Select(v => v != null ? v.Contains(',') ? @"""" + v.Replace(@"""", @"""""") + @"""" : v : "")));
	}
}
