using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text.RegularExpressions;
using NTextCat;

namespace NusRipper
{
	internal static class Languages
	{
		[Pure]
		public class LanguageCode : IEquatable<LanguageCode>
		{
			public readonly string Code;
			public readonly string AnglicisedName;

			internal LanguageCode(string code, string anglicisedName)
			{
				Code = code;
				AnglicisedName = anglicisedName;
			}

			[Pure]
			public bool Equals(LanguageCode other)
			{
				if (ReferenceEquals(null, other)) return false;
				if (ReferenceEquals(this, other)) return true;
				return string.Equals(Code, other.Code, StringComparison.InvariantCultureIgnoreCase);
			}

			[Pure]
			public override bool Equals(object obj)
			{
				if (ReferenceEquals(null, obj)) return false;
				if (ReferenceEquals(this, obj)) return true;
				if (obj.GetType() != this.GetType()) return false;
				return Equals((LanguageCode) obj);
			}

			[Pure]
			public override int GetHashCode()
				=> Code != null ? StringComparer.InvariantCultureIgnoreCase.GetHashCode(Code) : 0;

			[Pure]
			public static bool operator ==(LanguageCode left, LanguageCode right)
				=> Equals(left, right);

			[Pure]
			public static bool operator !=(LanguageCode left, LanguageCode right)
				=> !Equals(left, right);

			[Pure]
			public override string ToString()
				=> AnglicisedName;
		}

		public static readonly string ConfusingCharacters = @"[\.\?\!\:\;\'\""©™×\/\\]";

		public static readonly List<string> ConfusingProperNouns = new List<string>(new []
		{
			"Nintendo",
			"iQue",
			"3DS",
			"DSi",
			"DS",
			"TWL",
			"Banner",
			"TWLBanner1",
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
			"Publisher"
		});

		public static readonly LanguageCode English    = new LanguageCode("En", "English");
		public static readonly LanguageCode Japanese   = new LanguageCode("Ja", "Japanese");
		public static readonly LanguageCode French     = new LanguageCode("Fr", "French");
		public static readonly LanguageCode German     = new LanguageCode("De", "German");
		public static readonly LanguageCode Spanish    = new LanguageCode("Es", "Spanish");
		public static readonly LanguageCode Italian    = new LanguageCode("It", "Italian");
		public static readonly LanguageCode Dutch      = new LanguageCode("Nl", "Dutch");
		public static readonly LanguageCode Portuguese = new LanguageCode("Pt", "Portuguese");
		public static readonly LanguageCode Swedish    = new LanguageCode("Sv", "Swedish");
		public static readonly LanguageCode Norwegian  = new LanguageCode("No", "Norwegian");
		public static readonly LanguageCode Danish     = new LanguageCode("Da", "Danish");
		public static readonly LanguageCode Finnish    = new LanguageCode("Fi", "Finnish");
		public static readonly LanguageCode Chinese    = new LanguageCode("Zh", "Chinese");
		public static readonly LanguageCode Korean     = new LanguageCode("Ko", "Korean");
		public static readonly LanguageCode Polish     = new LanguageCode("Pl", "Polish");

		private static readonly Dictionary<string, LanguageCode> IdentifierLanguageDict = new Dictionary<string, LanguageCode>();

		static Languages()
		{
			IdentifierLanguageDict["en"] = English;
			IdentifierLanguageDict["ja"] = Japanese;
			IdentifierLanguageDict["fr"] = French;
			IdentifierLanguageDict["de"] = German;
			IdentifierLanguageDict["es"] = Spanish;
			IdentifierLanguageDict["it"] = Italian;
			IdentifierLanguageDict["zh"] = Chinese;
			IdentifierLanguageDict["ko"] = Korean;
		}

		private const string IdentifierDataPath = "LanguageDefs.xml";
		private static readonly char[] TrimChars = { ' ', '　', '\t', '\r', '\n', '\0' };
		
		private static readonly Lazy<RankedLanguageIdentifier> Identifier = new Lazy<RankedLanguageIdentifier>(() =>
		{
			RankedLanguageIdentifierFactory factory = new RankedLanguageIdentifierFactory();
			return factory.Load(IdentifierDataPath);
		});

		[Pure]
		public static List<LanguageCode> DetermineLanguage(string textChunk)
		{
			textChunk = textChunk?.Trim(TrimChars);

			if (string.IsNullOrWhiteSpace(textChunk))
				return null;

			IEnumerable<Tuple<LanguageInfo, double>> languages = Identifier.Value.Identify(textChunk);
			List<LanguageCode> retLanguages = languages.Select(l => IdentifierLanguageDict[l.Item1.Iso639_2T]).ToList();

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

			string sanitizedTitle = Regex.Replace(title, ConfusingCharacters, "");
			string[] titleLines = sanitizedTitle.Split('\n');
			if (titleLines.Length > 1)
				sanitizedTitle = string.Join(' ', titleLines.Take(titleLines.Length - 1));
			sanitizedTitle = string.Join(' ', sanitizedTitle.Split(' ', '　')
				.SelectMany(w => Regex.Split(w, @"(?<!^|[A-Z])(?=[A-Z])")) // Split CamelCase words into individual words
				.Except(ConfusingProperNouns) // Filter out confusing proper nouns
				.Where(w =>
				{
					string upperCase = w.ToUpperInvariant();
					return w != upperCase || (w.Length >= 4 && w == upperCase);
				})
				.Select(w =>
					(w.Length > 1
						? w[0].ToString().ToUpperInvariant() + w.Substring(1).ToLowerInvariant()
						: w.ToUpperInvariant()).Trim(TrimChars)));
			return sanitizedTitle.Trim();
		}
	}
}
