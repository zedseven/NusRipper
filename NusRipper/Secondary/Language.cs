using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text.RegularExpressions;
using NTextCat;

namespace NusRipper
{
	internal static class Language
	{
		public enum LanguageCodes
		{
			/// <summary>
			/// English
			/// </summary>
			En,
			/// <summary>
			/// Japanese
			/// </summary>
			Ja,
			/// <summary>
			/// French
			/// </summary>
			Fr,
			/// <summary>
			/// German
			/// </summary>
			De,
			/// <summary>
			/// Spanish
			/// </summary>
			Es,
			/// <summary>
			/// Italian
			/// </summary>
			It,
			/// <summary>
			/// Dutch
			/// </summary>
			Nl,
			/// <summary>
			/// Portuguese
			/// </summary>
			Pt,
			/// <summary>
			/// Swedish
			/// </summary>
			Sv,
			/// <summary>
			/// Norwegian
			/// </summary>
			No,
			/// <summary>
			/// Danish
			/// </summary>
			Da,
			/// <summary>
			/// Finnish
			/// </summary>
			Fi,
			/// <summary>
			/// Chinese
			/// </summary>
			Zh,
			/// <summary>
			/// Korean
			/// </summary>
			Ko,
			/// <summary>
			/// Polish
			/// </summary>
			Pl,
			/// <summary>
			/// Russian
			/// </summary>
			Ru
		}

		public const string ConfusingCharacters = @"[\.\?\!\:\;\'\""™®©×\/\\0-9]";

		public static readonly List<string> ConfusingProperNouns = new List<string>(new []
		{
			"Nintendo",
			"iQue",
			"3DS",
			"DSi",
			"DS",
			"TWL",
			"Twl",
			"Banner",
			"TWLBanner",
			"TWLBannerImage",
			"Mario",
			"Luigi",
			"Wario",
			"Waluigi",
			"WarioWare",
			"Zelda",
			"Link",
			"GmbH",
			"Default",
			"Title",
			"Subtitle",
			"Publisher",
			"default",
			"title",
			"subtitle",
			"publisher"
		});

		public static readonly List<string> CommonArticles = new List<string>(new[]
		{
			"the",
			"a",
			"an"
		});

		public const string RemoveTrademarkReminders = @"[™®©]";

		public static readonly Dictionary<LanguageCodes, string> LanguageAnglicisedNamesDict = new Dictionary<LanguageCodes, string>
		{
			[LanguageCodes.En] = "English",
			[LanguageCodes.Ja] = "Japanese",
			[LanguageCodes.Fr] = "French",
			[LanguageCodes.De] = "German",
			[LanguageCodes.Es] = "Spanish",
			[LanguageCodes.It] = "Italian",
			[LanguageCodes.Nl] = "Dutch",
			[LanguageCodes.Pt] = "Portuguese",
			[LanguageCodes.Sv] = "Swedish",
			[LanguageCodes.No] = "Norwegian",
			[LanguageCodes.Da] = "Danish",
			[LanguageCodes.Fi] = "Finnish",
			[LanguageCodes.Zh] = "Chinese",
			[LanguageCodes.Ko] = "Korean",
			[LanguageCodes.Pl] = "Polish",
			[LanguageCodes.Ru] = "Russian"
		};

		public static readonly Dictionary<string, LanguageCodes> IdentifierLanguageDict = new Dictionary<string, LanguageCodes>
		{
			["en"] = LanguageCodes.En,
			["ja"] = LanguageCodes.Ja,
			["fr"] = LanguageCodes.Fr,
			["de"] = LanguageCodes.De,
			["es"] = LanguageCodes.Es,
			["it"] = LanguageCodes.It,
			["nl"] = LanguageCodes.Nl,
			["pt"] = LanguageCodes.Pt,
			["sv"] = LanguageCodes.Sv,
			["no"] = LanguageCodes.No,
			["da"] = LanguageCodes.Da,
			["fi"] = LanguageCodes.Fi,
			["zh"] = LanguageCodes.Zh,
			["ko"] = LanguageCodes.Ko,
			["pl"] = LanguageCodes.Pl,
			["ru"] = LanguageCodes.Ru
		};

		private const string IdentifierDataPath = "ReferenceFiles/LanguageDefs.xml";
		private static readonly char[] TrimChars = { ' ', '　', '\t', '\r', '\n', '\0' };
		
		private static readonly Lazy<RankedLanguageIdentifier> Identifier = new Lazy<RankedLanguageIdentifier>(() =>
		{
			RankedLanguageIdentifierFactory factory = new RankedLanguageIdentifierFactory();
			return factory.Load(IdentifierDataPath);
		});

		[Pure]
		public static List<LanguageCodes> DetermineLanguage(string textChunk)
		{
			textChunk = textChunk?.Trim(TrimChars);

			if (string.IsNullOrWhiteSpace(textChunk))
				return null;

			IEnumerable<Tuple<LanguageInfo, double>> languages = Identifier.Value.Identify(textChunk);
			List<LanguageCodes> retLanguages = languages.Select(l => IdentifierLanguageDict[l.Item1.Iso639_2T.ToLowerInvariant()]).ToList();

			if (retLanguages.Count <= 0)
			{
				Log.Instance.Warn($"The most likely language for the text chunk '{textChunk}' could not be determined with an acceptable level of certainty.");
				return null;
			}

			Log.Instance.Trace($"The most likely language for the text chunk '{textChunk}' is {retLanguages[0]}.");
			return retLanguages;
		}

		[Pure]
		public static string SanitizeRomTitleForLanguageDetermination(string title)
		{
			if (string.IsNullOrWhiteSpace(title))
				return null;

			string sanitizedTitle = Regex.Replace(title, ConfusingCharacters, "");    // Filter out confusing characters
			string[] titleLines = sanitizedTitle.Split('\n');
			if (titleLines.Length > 1)                                                // Remove the publisher if necessary
				sanitizedTitle = string.Join(' ', titleLines.Take(titleLines.Length - 1));
			sanitizedTitle = string.Join(' ',
				sanitizedTitle.Split(' ', '　')
				.SelectMany(w => Regex.Split(w, @"(?<!^|[A-Z])(?=[A-Z])")) // Split CamelCase words into individual words
				.Except(ConfusingProperNouns)                                         // Filter out confusing proper nouns
				.Where(w =>
				{
					string upperCase = w.ToUpperInvariant();
					return w != upperCase || (w.Length >= 4 && w == upperCase);
				})                                                                    // Remove acronyms
				.ToTitleCase());                                                      // Title Case words
			return sanitizedTitle.Trim();
		}

		[Pure]
		public static string ToTitleCase(this string str)
			=> string.Join(' ', str.Split(' ').ToTitleCase());

		[Pure]
		public static IEnumerable<string> ToTitleCase(this IEnumerable<string> strs)
			=> strs.Select(str => (str.Length > 1
				? str[0].ToString().ToUpperInvariant() + str.Substring(1).ToLowerInvariant()
				: str.ToUpperInvariant()).Trim(TrimChars));
	}
}
