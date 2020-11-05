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
		private const string SerializationDatFile = "allTitleInfo.dat";

		// I've tried really hard to keep the functions of this tool generic and largely system-agnostic, but unfortunately I had to do it this way
		// ReSharper disable twice IdentifierTypo
		// ReSharper disable twice InconsistentNaming
		private enum DumperNames
		{
			zedseven,
			Galaxy,
			Larsenv
		}

		/// <summary>
		/// The canonical order that languages should be listed in for DAT-o-MATIC dat files.<br />
		/// As per: <a href='https://datomatic.no-intro.org/stuff/The%20Official%20No-Intro%20Convention%20(20071030).pdf'>https://datomatic.no-intro.org/stuff/The%20Official%20No-Intro%20Convention%20(20071030).pdf</a>
		/// </summary>
		private static readonly List<Language.LanguageCodes> LanguageOrder = new List<Language.LanguageCodes>(new []
		{
			Language.LanguageCodes.En,
			Language.LanguageCodes.Ja,
			Language.LanguageCodes.Fr,
			Language.LanguageCodes.De,
			Language.LanguageCodes.Es,
			Language.LanguageCodes.It,
			Language.LanguageCodes.Nl,
			Language.LanguageCodes.Pt,
			Language.LanguageCodes.Sv,
			Language.LanguageCodes.No,
			Language.LanguageCodes.Da,
			Language.LanguageCodes.Fi,
			Language.LanguageCodes.Zh,
			Language.LanguageCodes.Ko,
			Language.LanguageCodes.Pl
		});

		[Pure]
		private static Region.Regions RegionFromCharCode(char charCode)
			=> charCode switch
			{
				'E' => Region.Regions.USA,
				'J' => Region.Regions.Japan,
				'P' => Region.Regions.Europe,
				'U' => Region.Regions.Australia,
				'K' => Region.Regions.Korea,
				'V' => Region.Regions.Europe | Region.Regions.Australia,
				'C' => Region.Regions.China,
				'D' => Region.Regions.Germany,
				'F' => Region.Regions.France,
				'I' => Region.Regions.Italy,
				'S' => Region.Regions.Spain,
				'O' => Region.Regions.USA | Region.Regions.Europe,
				'X' => Region.Regions.Europe, // likely South America, and originally "Unknown" - all 4 DSiWare titles that use it have GB,FR,DE,IT,ES shop pages, though often don't use GB or ES
				'T' => Region.Regions.USA | Region.Regions.Australia,
				'H' => Region.Regions.Netherlands,
				'A' => Region.Regions.World,
				_ => Region.Regions.World
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

		// Data-Caching Classes
		[Serializable]
		private sealed class FullTitleInfo
		{
			// Title Metadata
			public string TitleId;
			public string TitleIdLower => TitleId.ToLowerInvariant();
			public string TitleIdGameCode;
			public char RegionCode;
			public Region.Regions Region;
			public string NoIntroTitle;
			public bool System;
			public bool Deleted;
			public HashSet<Language.LanguageCodes> Languages = new HashSet<Language.LanguageCodes>();
			public List<Language.LanguageCodes> NebulousLanguages = new List<Language.LanguageCodes>();
			public RomInfo.TitleIndices PrimaryLanguageIndex;
			public bool Found3dsPort;

			// Title File Info
			public List<(string, ushort)> TicketFiles = new List<(string, ushort)>();
			public List<(string fileName, TitleMetadata metadata)> MetadataFiles = new List<(string fileName, TitleMetadata metadata)>();
			public List<string> ContentEncryptedFiles = new List<string>();
			public Dictionary<string, (string fileName, RomInfo info, int metadataIndex)> ContentDecryptedFiles = new Dictionary<string, (string fileName, RomInfo info, int metadataIndex)>();

			// Working Variables
			public string TitleDir;
			public int NewestMetadataIndex;
			public string NewestContentId;
		}

		private sealed class RomEntry
		{
			public enum EntryTypes
			{
				Ticket,
				Metadata,
				Content
			}

			public readonly EntryTypes Type;
			public readonly bool Encrypted;
			public readonly string FileName;
			public readonly string DisplayName;
			public readonly Hasher.FileHashCollection Hashes;
			public readonly DateTime? ModTime;
			public readonly string VersionStr;
			public readonly string SerialStr;
			public readonly DumperNames? SpecificDumper;

			public RomEntry(EntryTypes type,
				bool encrypted,
				string fileName,
				Hasher.FileHashCollection hashes,
				DateTime? modTime = null,
				string versionStr = null,
				string serialStr = null,
				string displayName = null,
				DumperNames? specificDumper = null)
			{
				Type = type;
				Encrypted = encrypted;
				FileName = fileName;
				DisplayName = displayName;
				Hashes = hashes;
				ModTime = modTime;
				VersionStr = versionStr;
				SerialStr = serialStr;
				SpecificDumper = specificDumper;
			}
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

			List<FullTitleInfo> allTitleInfo = new List<FullTitleInfo>();
			if (!File.Exists(SerializationDatFile))
			{
				// Run through all titles and collect their information
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
							titleInfo.TicketFiles.Add((fileName, TicketBooth.GetTicketVersion(filePath)));
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
					Region.Regions primaryRegion = titleInfo.Region.GetPrimaryRegion();

					// Languages
					string primaryTitle = null;
					titleInfo.Languages = new HashSet<Language.LanguageCodes>();
					titleInfo.NebulousLanguages = new List<Language.LanguageCodes>();

					// If the DSiWare title was ported to 3DS, attempt to find it on the eShop to get language information
					// This is basically just going through the various region 'storefronts' for the title - all should have
					// the same information, just in different languages, though only valid regions for the game have anything at all
					Language.LanguageCodes[] title3dsLanguages = null;
					foreach (Region.Regions reg in titleInfo.Region
						.DecomposeRegion()                                                       // Break apart things like "Europe, Australia" into their individual regions
						.Reverse()                                                               // Reverse because often regions are "Europe, ..." and greater specificity is preferred
						.SelectMany(r => new [] { r }.Concat(Region.RegionRelatedRegionsMap[r])) // Get all related regions
						.Distinct())                                                             // No duplicates
					{
						if (Region.RegionTwoLetterCodes.TryGetValue(reg, out string twoLetter))
							title3dsLanguages = await Port3dsInfo.GetTitle3dsInfo(titleInfo.TitleId, twoLetter);
						if (title3dsLanguages != null)
							break;
					}

					titleInfo.Found3dsPort = title3dsLanguages != null;
					Language.LanguageCodes[] regionLangs = Region.GetExpectedLanguages(titleInfo.Region);
					if (titleInfo.Found3dsPort)
					{
						Log.Instance.Debug($"Title '{titleInfo.TitleId}' has an existing 3DS port, with languages \"{string.Join(", ", title3dsLanguages)}\".");
						title3dsLanguages.ForEach(l => titleInfo.Languages.Add(l));
					}
					if (titleInfo.Languages.Count <= 0)
					{
						if (title3dsLanguages == null && Port3dsInfo.Has3dsPort(titleInfo.TitleId))
							Log.Instance.Warn($"Title '{titleInfo.TitleId}' is supposed to have a 3DS port, but nothing was found. Deriving language and title information from the latest ROM.");
						else
							Log.Instance.Debug($"Title '{titleInfo.TitleId}' does not have an existing 3DS port or the supported languages were not able to be found. Deriving language and title information from the latest ROM.");

						Tuple<string, int, HashSet<Language.LanguageCodes>>[] titleGroups = titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].info.Titles
							.Select((t, i) => (t: Language.SanitizeRomTitleForLanguageDetermination(t), i))
							.GroupBy(t => t.t)
							.Select(e => new Tuple<string, int, HashSet<Language.LanguageCodes>>(
								e.Key,
								e.First().i,
								e.Select(v => RomInfo.TitleIndexToLanguageCode(v.i))
									.ToHashSet()))
							.ToArray();

						foreach (Tuple<string, int, HashSet<Language.LanguageCodes>> titleEntry in titleGroups.Where(
							g => g.Item3.Count == 1))
							titleInfo.Languages.Add(RomInfo.TitleIndexToLanguageCode(titleEntry.Item2));

						Tuple<string, int, HashSet<Language.LanguageCodes>>[] titleConflicts = titleGroups
							.Where(g => g.Item3.Count > 1)
							.ToArray();
						for (int i = 0; i < titleConflicts.Length; i++)
						{
							if (string.IsNullOrWhiteSpace(titleConflicts[i].Item1))
								continue;
							string sanitizedTitle = titleConflicts[i].Item1;

							Log.Instance.Trace(
								$"Title '{titleInfo.TitleId}' has a title conflict for title '{sanitizedTitle}' ({titleConflicts[i].Item3} titles are the same).");

							List<Language.LanguageCodes> langs = Language.DetermineLanguage(sanitizedTitle);
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
								Language.LanguageCodes defaultLang = Region.RegionExpectedLanguageMap[primaryRegion][0];
								Log.Instance.Warn(
									$"No languages were found or determined for the title '{titleInfo.TitleId}'s conflict \"{titleConflicts[i]}\". Proceeding with the default language for the region ({titleInfo.Region}), {defaultLang}.");
								titleInfo.Languages.Add(defaultLang);
								titleInfo.NebulousLanguages.Add(defaultLang);
							}
						}

						if (titleInfo.Languages.Count <= 0)
						{
							Language.LanguageCodes defaultLang = Region.RegionExpectedLanguageMap[primaryRegion][0];
							Log.Instance.Warn(
								$"No languages were found or determined for the title '{titleInfo.TitleId}'. Proceeding with the default language for the region ({titleInfo.Region}), {defaultLang}.");
							titleInfo.Languages.Add(defaultLang);
							titleInfo.NebulousLanguages.Add(defaultLang);
						}
					}

					// No-Intro Title
					Language.LanguageCodes primaryLanguage = titleInfo.Languages.OrderBy(x =>
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
				await using (FileStream sfs = new FileStream(SerializationDatFile, FileMode.Create))
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
			}
			else
			{
				// Load the cached results so everything doesn't have to be re-downloaded
				Log.Instance.Info($"Loading the cached results at \"{Path.Combine(Constants.AssemblyPath, SerializationDatFile)}\".");
				await using FileStream sfs = File.OpenRead(SerializationDatFile);
				BinaryFormatter formatter = new BinaryFormatter();
				try
				{
					allTitleInfo = (List<FullTitleInfo>) formatter.Deserialize(sfs);
				}
				catch (SerializationException e)
				{
					Log.Instance.Fatal($"Unable to deserialize the title info. Reason: {e.Message}");
					return;
				}
			}

			Stopwatch datStopwatch = Stopwatch.StartNew();
			Log.Instance.Info("Beginning the dat file creation.");

			XmlWriterSettings xSettings = new XmlWriterSettings
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

			await using (FileStream xfs = new FileStream(outputXmlPath, FileMode.Create))
			using (XmlWriter xWriter = XmlWriter.Create(xfs, xSettings))
			await using (FileStream mfs = writeMedia ? new FileStream(outputMediaPath, FileMode.Create) : null)
			await using (StreamWriter mWriter = writeMedia ? new StreamWriter(mfs, Encoding.UTF8) : null)
			await using (FileStream tfs = writeMedia ? new FileStream(Path.Combine(Path.GetDirectoryName(outputMediaPath), "titles.csv"), FileMode.Create) : null)
			await using (StreamWriter tWriter = writeMedia ? new StreamWriter(tfs, Encoding.UTF8) : null)
			{
				// Write the preceeding file info
				await xWriter.WriteStartDocumentAsync();

				await xWriter.WriteRawAsync("\r\n<!DOCTYPE datafile PUBLIC \"http://www.logiqx.com/Dats/datafile.dtd\" \"-//Logiqx//DTD ROM Management Datafile//EN\">\r\n");

				xWriter.WriteStartElement("datafile");
				xWriter.WriteStartElement("header");
				await xWriter.WriteEndElementAsync();

				if (writeMedia)
				{
					await WriteCsvRow(mWriter, "Archive ID", "Language Determination Method",
						"Nebulously-Determined Languages", "Only Title",
						"English", "Japanese", "French", "German", "Spanish", "Italian", "Chinese", "Korean");
					await WriteCsvRow(tWriter, "Title ID", "No-Intro Title");
				}

				// Load Larsen and Galaxy's dat file lists
				HashSet<(string titleId, string fileName)> DatFilesSetLarsen = new HashSet<(string, string)>();
				LoadCsvIntoDatFileList(Path.Combine(Constants.ReferenceFilesPath, "LarsenDatFiles.csv"), DatFilesSetLarsen);
				HashSet<(string titleId, string fileName)> DatFilesSetGalaxy = new HashSet<(string, string)>();
				LoadCsvIntoDatFileList(Path.Combine(Constants.ReferenceFilesPath, "GalaxyDatFiles.csv"), DatFilesSetGalaxy);
				Dictionary<string, RomEntry> GalaxyMismatchedTickets = new Dictionary<string, RomEntry>();
				Helpers.LoadCsvIntoDictionary(Path.Combine(Constants.ReferenceFilesPath, "GalaxyMismatchedTickets.csv"),
					GalaxyMismatchedTickets,
					t => t,
					h =>
					{
						string[] hashes = h.Split(' ');
						return new RomEntry(RomEntry.EntryTypes.Ticket,
							true,
							Ripper.TicketFileName,
							new Hasher.FileHashCollection(null, hashes[0], hashes[1], hashes[2], null),
							null,
							null,
							null,
							$"{Ripper.TicketFileName}.{hashes[0]}",
							DumperNames.Galaxy);
					});
				Dictionary<(string titleId, string fileName), DateTime> GalaxyFileDates = new Dictionary<(string titleId, string fileName), DateTime>();
				Helpers.LoadCsvIntoDictionary(Path.Combine(Constants.ReferenceFilesPath, "GalaxyFileDates.csv"),
					GalaxyFileDates,
					s =>
					{
						string[] parts = s.Split(' ');
						return (parts[0], parts[1]);
					},
					d => DateTime.Parse(d, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

				// Prepare the title info
				IEnumerable<(string regionAgnosticKey, List<FullTitleInfo> titles)> preparedTitleInfo = allTitleInfo
					.GroupBy(t => t.TitleId.Substring(0, 14))                                  // Collect titles into region-agnostic groups (only the region code (last 2 chars) changes for regional releases of a title)
					.Select(g => (regionAgnosticKey: g.Key, titles: g
						.OrderBy(t => t.Languages.Contains(Language.LanguageCodes.En) ? 0 : 1) // Releases containing English are preferred as the parent copy, as No-Intro seems to be Europe-biased
						.ThenByDescending(t => t.Languages.Count)                              // Releases with the most supported languages are preferred
						.ThenBy(t =>
						{
							int index = Array.IndexOf(CharCodeRegionOrder, t.RegionCode);
							return index > -1 ? index : int.MaxValue;
						})                                                                     // If a tie still hasn't been broken, choose based on region code, with preference for greater coverage by the region
						.ToList()));

				// Build the dat file
				int archiveId = StartingArchiveId;
				foreach ((string regionAgnosticKey, List<FullTitleInfo> titles) titleGroup in preparedTitleInfo)
				{
					int parentArchiveId = archiveId;
					for (int i = 0; i < titleGroup.titles.Count; i++)
					{
						FullTitleInfo titleInfo = titleGroup.titles[i];
						string archiveNum = archiveId.ToString().PadLeft(4, '0');

						Log.Instance.Info($"{archiveNum} - {titleInfo.TitleId} - {titleInfo.TitleIdGameCode}");

						// Write the XML
						xWriter.WriteStartElement("game");
						await xWriter.WriteAttributeAsync("name", titleInfo.NoIntroTitle);
						xWriter.WriteStartElement("archive");
						await xWriter.WriteAttributeAsync("number",         archiveNum);
						await xWriter.WriteAttributeAsync("name",           titleInfo.NoIntroTitle);
						await xWriter.WriteAttributeAsync("namealt",        "");
						await xWriter.WriteAttributeAsync("region",         titleInfo.Region.ToString());
						await xWriter.WriteAttributeAsync("languages",
							string.Join(',', titleInfo.Languages.OrderBy(l =>
							{
								int index = LanguageOrder.IndexOf(l);
								return index > -1 ? index : int.MaxValue;
							})));
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

						// Build a list of all ROM entries to write
						List<RomEntry> romEntries = new List<RomEntry>();

						foreach ((string ticketFile, ushort version) in titleInfo.TicketFiles)
						{
							(Hasher.FileHashCollection hashes, DateTime? modTime) = GetFileMetaInfo(Path.Combine(titleInfo.TitleDir, ticketFile));
							string displayName = $"{ticketFile}.{version}";
							string versionStr = $"{version},{TitleMetadata.VersionToHumanReadable(version)}";
							
							romEntries.Add(new RomEntry(RomEntry.EntryTypes.Ticket,
								true,
								ticketFile,
								hashes,
								modTime,
								versionStr,
								displayName: displayName));
							romEntries.Add(new RomEntry(RomEntry.EntryTypes.Ticket,
								false,
								ticketFile,
								hashes,
								modTime,
								versionStr,
								displayName: displayName));
						}

						foreach ((string fileName, TitleMetadata metadata) metadata in titleInfo.MetadataFiles)
						{
							(Hasher.FileHashCollection hashes, DateTime? modTime) = GetFileMetaInfo(Path.Combine(titleInfo.TitleDir, metadata.fileName));
							string versionStr = $"{metadata.metadata.TitleVersion},{TitleMetadata.VersionToHumanReadable(metadata.metadata.TitleVersion)}";

							romEntries.Add(new RomEntry(RomEntry.EntryTypes.Metadata,
								true,
								metadata.fileName,
								hashes,
								modTime,
								versionStr));
							romEntries.Add(new RomEntry(RomEntry.EntryTypes.Metadata,
								false,
								metadata.fileName,
								hashes,
								modTime,
								versionStr));
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
							string serialStr = string.Join(',', serialParts);

							string versionStr = $"{titleInfo.MetadataFiles[contentFileEntry.Value.metadataIndex].metadata.TitleVersion},{TitleMetadata.VersionToHumanReadable(titleInfo.MetadataFiles[contentFileEntry.Value.metadataIndex].metadata.TitleVersion)}";

							(Hasher.FileHashCollection hashesEnc, DateTime? modTimeEnc) = GetFileMetaInfo(Path.Combine(titleInfo.TitleDir, contentFileEntry.Key));
							romEntries.Add(new RomEntry(RomEntry.EntryTypes.Content,
								true,
								contentFileEntry.Key,
								hashesEnc,
								modTimeEnc,
								versionStr,
								serialStr));

							Hasher.FileHashCollection hashesDec = new Hasher.FileHashCollection(Path.Combine(titleInfo.TitleDir, contentFileEntry.Value.fileName));
							DateTime modTimeDec = File.GetLastWriteTime(Path.Combine(titleInfo.TitleDir, contentFileEntry.Value.fileName));
							romEntries.Add(new RomEntry(RomEntry.EntryTypes.Content,
								false,
								contentFileEntry.Key,
								hashesDec,
								modTimeDec,
								versionStr,
								serialStr));
						}

						// Source entries for each dumper involved in the project, to capture their work and give them appropriate credit
						// it's a bit of a mess, but this was the cleanest way to do it without an exorbitant amount of extra work
						foreach (DumperNames dumperName in Enum.GetValues(typeof(DumperNames)))
						{
							List<(string, List<RomEntry>)> dumperEntries = new List<(string, List<RomEntry>)>();

							switch (dumperName)
							{
								case DumperNames.zedseven:
									dumperEntries.Add((File.GetLastWriteTime(Path.Combine(titleInfo.TitleDir,
												titleInfo.MetadataFiles[titleInfo.NewestMetadataIndex].fileName))
											.ToString("yyyy-MM-dd"),
										romEntries.Where(e =>
											!titleInfo.Deleted || !e.Encrypted).ToList()));
									break;
								case DumperNames.Galaxy:
									List<RomEntry> ungroupedEntries = romEntries.Where(e =>
										e.Encrypted &&
										DatFilesSetGalaxy.Contains((titleInfo.TitleIdLower, e.FileName))).ToList();

									// Swap out tickets for the Galaxy-specific hashes if necessary
									if (GalaxyMismatchedTickets.TryGetValue(titleInfo.TitleIdLower, out RomEntry mismatchedTicketEntry))
									{
										int ticketIndex = -1;
										for (int j = 0; j < ungroupedEntries.Count; j++)
											if (ungroupedEntries[j].FileName == mismatchedTicketEntry.FileName &&
											    ungroupedEntries[j].Encrypted == mismatchedTicketEntry.Encrypted)
											{
												ticketIndex = j;
												break;
											}

										ungroupedEntries.Replace(ticketIndex, mismatchedTicketEntry);
									}

									// Group the entries by their dump dates
									dumperEntries.AddRange(
										ungroupedEntries
											.GroupBy(e =>
											{
												if (GalaxyFileDates.TryGetValue((titleInfo.TitleIdLower, e.FileName),
													out DateTime dumpDate))
													return (DateTime?) dumpDate.Date;
												return null;
											})
											.OrderBy(g => g.Key ?? DateTime.MaxValue)
											.Select(g => (g.Key?.ToString("yyyy-MM-dd"), g.ToList())));
									break;
								default:
								case DumperNames.Larsenv:
									dumperEntries.Add(("2018-11-17",
										romEntries.Where(e =>
												e.Encrypted &&
												DatFilesSetLarsen.Contains((titleInfo.TitleIdLower, e.FileName)))
											.ToList()));
									break;
							}

							foreach ((string dumpDate, List<RomEntry> sourceEntries) in dumperEntries)
							{
								if (sourceEntries.Count <= 0)
									continue;

								xWriter.WriteStartElement("source");
								xWriter.WriteStartElement("details");
								await xWriter.WriteAttributeAsync("section",          "Trusted Dump");
								await xWriter.WriteAttributeAsync("rominfo",          "");
								await xWriter.WriteAttributeAsync("dumpdate",         dumpDate);
								await xWriter.WriteAttributeAsync("knowndumpdate",
									dumperName == DumperNames.zedseven ||
									dumperName == DumperNames.Galaxy && dumpDate != null
										? "1"
										: "0");
								await xWriter.WriteAttributeAsync("releasedate",      dumpDate);
								await xWriter.WriteAttributeAsync("knownreleasedate", "0");
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

								foreach (RomEntry entry in sourceEntries)
									await WriteRomEntry(xWriter, entry);

								await xWriter.WriteEndElementAsync();
							}
						}

						await xWriter.WriteEndElementAsync();

						// Write the row for the Media CSV
						if (writeMedia)
						{
							await WriteCsvRow(mWriter,
								archiveNum,
								titleInfo.Found3dsPort ? "3DS eShop Port" : "Guessed From ROM Titles",
								string.Join(',', titleInfo.NebulousLanguages),
								titleInfo.Languages.Count == 1                          ? titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].info.GetTitleOnly(titleInfo.PrimaryLanguageIndex)               : "",
								titleInfo.Languages.Contains(Language.LanguageCodes.En) ? titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].info.GetTitleOnly(RomInfo.TitleIndices.EnglishIndex)  : "",
								titleInfo.Languages.Contains(Language.LanguageCodes.Ja) ? titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].info.GetTitleOnly(RomInfo.TitleIndices.JapaneseIndex) : "",
								titleInfo.Languages.Contains(Language.LanguageCodes.Fr) ? titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].info.GetTitleOnly(RomInfo.TitleIndices.FrenchIndex)   : "",
								titleInfo.Languages.Contains(Language.LanguageCodes.De) ? titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].info.GetTitleOnly(RomInfo.TitleIndices.GermanIndex)   : "",
								titleInfo.Languages.Contains(Language.LanguageCodes.Es) ? titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].info.GetTitleOnly(RomInfo.TitleIndices.SpanishIndex)  : "",
								titleInfo.Languages.Contains(Language.LanguageCodes.It) ? titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].info.GetTitleOnly(RomInfo.TitleIndices.ItalianIndex)  : "",
								titleInfo.Languages.Contains(Language.LanguageCodes.Zh) ? titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].info.GetTitleOnly(RomInfo.TitleIndices.ChineseIndex)  : "",
								titleInfo.Languages.Contains(Language.LanguageCodes.Ko) ? titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].info.GetTitleOnly(RomInfo.TitleIndices.KoreanIndex)   : "");
							await WriteCsvRow(tWriter, titleInfo.TitleId, titleInfo.NoIntroTitle);
						}

						// Make user-friendly NDS files of the latest version of each title
						if (makeNdsFiles)
							File.Copy(Path.Combine(titleInfo.TitleDir, titleInfo.ContentDecryptedFiles[titleInfo.NewestContentId].fileName),
								Path.Combine(titleInfo.TitleDir, $"{titleInfo.NoIntroTitle} ({titleInfo.Region.ToString()}).nds"),
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

		private static async Task WriteRomEntry(XmlWriter writer, RomEntry entry)
		{
			writer.WriteStartElement("rom");
			await writer.WriteAttributeAsync("dirname",   "");
			await writer.WriteAttributeAsync("forcename", entry.DisplayName ?? entry.FileName);
			await writer.WriteAttributeAsync("extension", "");
			await writer.WriteAttributeAsync("item",      "");
			await writer.WriteAttributeAsync("date",      entry.ModTime?.ToString("yyyy-MM-dd"));
			await writer.WriteAttributeAsync("format",    entry.Encrypted ? "CDN" : "CDNdec");
			await writer.WriteAttributeAsync("version",   entry.VersionStr);
			await writer.WriteAttributeAsync("utype",     "");
			await writer.WriteAttributeAsync("size",      entry.Hashes.FileSize.ToString());
			await writer.WriteAttributeAsync("crc",       entry.Hashes.Crc32String);
			await writer.WriteAttributeAsync("md5",       entry.Hashes.Md5String);
			await writer.WriteAttributeAsync("sha1",      entry.Hashes.Sha1String);
			await writer.WriteAttributeAsync("sha256",    entry.Hashes.Sha256String);
			await writer.WriteAttributeAsync("serial",    entry.SerialStr);
			await writer.WriteAttributeAsync("bad",       "0");
			await writer.WriteEndElementAsync();
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
		public static string GetNoIntroTitle(string title, string subTitle, Language.LanguageCodes language)
		{
			StringBuilder retTitle = new StringBuilder();
			bool doSubTitle = !string.IsNullOrWhiteSpace(subTitle);

			// Romanize title if necessary
			if (language == Language.LanguageCodes.Ja)
			{
				title = Japanese.KanjiReadings.Value.ProcessWithKana(title, Japanese.ModifiedHepburn.Value);
				if (doSubTitle)
					subTitle = Japanese.KanjiReadings.Value.ProcessWithKana(subTitle, Japanese.ModifiedHepburn.Value);
			}
			else if (language == Language.LanguageCodes.Zh)
			{
				title = Chinese.HanyuPinyin.Value.Process(title);
				if (doSubTitle)
					subTitle = Chinese.HanyuPinyin.Value.Process(subTitle);
			}
			else if (language == Language.LanguageCodes.Ko)
			{
				title = Korean.HanjaReadings.Value.Process(title, Korean.RevisedRomanization.Value);
				if (doSubTitle)
					subTitle = Korean.HanjaReadings.Value.Process(subTitle, Korean.RevisedRomanization.Value);
			}

			title = Regex.Replace(Regex.Replace(title,
							Language.RemoveTrademarkReminders, "") // Remove Trademark Reminders
						.FoldToAscii(),                             // Convert to Low ASCII
					RemoveNoIntroDisallowedChars, "")               // Remove additional Low ASCII characters that are not allowed
				.Trim(' ', ':', '-');                               // Remove sub title separation characters and spaces

			// Move common articles at the beginning of the title to the end (ie. "The Legend of Zelda" to "Legend of Zelda, The")
			foreach (string commonArticle in Language.CommonArticles)
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
								Language.RemoveTrademarkReminders, "") // Remove Trademark Reminders
							.FoldToAscii(),                             // Convert to Low ASCII
						RemoveNoIntroDisallowedChars, "")               // Remove additional Low ASCII characters that are not allowed
					.Trim(' ', ':', '-');                               // Remove sub title separation characters and spaces

				retTitle.Append($" - {subTitle}");
			}

			return Regex.Replace(retTitle.ToString(), @"\s+", " ") // Remove duplicate whitespace
				.Trim(' ', '.');                                   // Trim off disallowed start/end characters
		}

		private static async Task WriteCsvRow(TextWriter writer, params string[] values)
			=> await writer.WriteLineAsync(string.Join(',',
				values.Select(v => v != null ? v.Contains(',') ? @"""" + v.Replace(@"""", @"""""") + @"""" : v : "")));

		private static bool LoadCsvIntoDatFileList(string path, HashSet<(string, string)> set)
		{
			using FileStream stream = File.OpenRead(path);

			if (!stream.CanRead)
				return false;

			try
			{
				using StreamReader reader = new StreamReader(stream);

				// Discard the first line, since it's simply the heading
				_ = reader.ReadLine();

				while (!reader.EndOfStream)
				{
					string line = reader.ReadLine();
					if (string.IsNullOrWhiteSpace(line) || line[0] == '#')
						continue;

					int commaIndex = line.IndexOf(',');

					set.Add((line.Substring(0, commaIndex), line.Substring(commaIndex + 1)));
				}
			}
			catch (Exception e)
			{
				return false;
			}

			return true;
		}
	}
}
