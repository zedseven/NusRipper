using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;

namespace NusRipper
{
	internal static class Region
	{
		// ReSharper disable once InconsistentNaming
		[Flags]
		public enum Regions
		{
			Unknown      = 0,
			USA          = 1,
			Japan        = 1 << 1,
			Europe       = 1 << 2,
			Australia    = 1 << 3,
			Korea        = 1 << 4,
			China        = 1 << 5,
			Germany      = 1 << 6,
			France       = 1 << 7,
			Italy        = 1 << 8,
			Spain        = 1 << 9,
			Netherlands  = 1 << 10,
			SouthAmerica = 1 << 11,
			World        = 1 << 12
		}

		// https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2
		public static readonly Dictionary<Regions, string> RegionTwoLetterCodes = new Dictionary<Regions, string>
		{
			[Regions.USA]          = "US",
			[Regions.Japan]        = "JP",
			[Regions.Europe]       = "GB", // I know this isn't correct, but Nintendo has no Great Britain region - Europe is the closest.
			[Regions.Australia]    = "AU",
			[Regions.Korea]        = "KR",
			[Regions.China]        = "CN",
			[Regions.Germany]      = "DE",
			[Regions.France]       = "FR",
			[Regions.Italy]        = "IT",
			[Regions.Spain]        = "ES",
			[Regions.Netherlands]  = "NL",
			[Regions.SouthAmerica] = "BR"
		};
		public static readonly Dictionary<Regions, Language.LanguageCodes[]> RegionExpectedLanguageMap = new Dictionary<Regions, Language.LanguageCodes[]>
		{
			[Regions.USA]         = new[] { Language.LanguageCodes.En, Language.LanguageCodes.Ja, Language.LanguageCodes.Es },
			[Regions.Japan]       = new[] { Language.LanguageCodes.Ja, Language.LanguageCodes.En },
			[Regions.Europe]      = new[] { Language.LanguageCodes.En, Language.LanguageCodes.Fr, Language.LanguageCodes.De, Language.LanguageCodes.It },
			[Regions.Australia]   = new[] { Language.LanguageCodes.En, Language.LanguageCodes.Ja },
			[Regions.Korea]       = new[] { Language.LanguageCodes.Ko, Language.LanguageCodes.Ja, Language.LanguageCodes.En },
			[Regions.China]       = new[] { Language.LanguageCodes.Zh, Language.LanguageCodes.Ja, Language.LanguageCodes.En },
			[Regions.Germany]     = new[] { Language.LanguageCodes.De, Language.LanguageCodes.Ja },
			[Regions.France]      = new[] { Language.LanguageCodes.Fr, Language.LanguageCodes.En, Language.LanguageCodes.Ja },
			[Regions.Italy]       = new[] { Language.LanguageCodes.It, Language.LanguageCodes.En },
			[Regions.Spain]       = new[] { Language.LanguageCodes.Es },
			[Regions.Netherlands] = new[] { Language.LanguageCodes.De },
			[Regions.World]       = new[] { Language.LanguageCodes.En, Language.LanguageCodes.Ja }
		};
		public static readonly Dictionary<Regions, Regions[]> RegionRelatedRegionsMap = new Dictionary<Regions, Regions[]>
		{
			[Regions.USA]         = new[] { Regions.Europe, Regions.SouthAmerica },
			[Regions.Japan]       = new[] { Regions.Europe, Regions.USA },
			[Regions.Europe]      = new[] { Regions.France, Regions.Italy, Regions.Germany, Regions.Netherlands, Regions.Spain, Regions.USA, Regions.SouthAmerica }, // You may be wondering why the USA is here - apparently "Ivy the Kiwi? Mini", even though it's a Europe title, has only a USA eShop storefront
			[Regions.Australia]   = new[] { Regions.Europe },
			[Regions.Korea]       = new[] { Regions.Japan, Regions.China },
			[Regions.China]       = new[] { Regions.Japan, Regions.Korea },
			[Regions.Germany]     = new[] { Regions.Netherlands, Regions.Europe },
			[Regions.France]      = new[] { Regions.Europe },
			[Regions.Italy]       = new[] { Regions.Europe },
			[Regions.Spain]       = new[] { Regions.Europe },
			[Regions.Netherlands] = new[] { Regions.Germany, Regions.Europe },
			[Regions.World]       = new[] { Regions.Europe, Regions.USA }
		};

		[Pure]
		public static IEnumerable<Regions> DecomposeRegion(this Regions region)
			=> Enum.GetValues(typeof(Regions))
				.Cast<Regions>()
				.Where(r => r != Regions.Unknown && region.HasFlag(r));

		[Pure]
		public static Language.LanguageCodes[] GetExpectedLanguages(Regions region)
			=> region != Regions.Unknown
				? DecomposeRegion(region)
					.SelectMany(r => RegionExpectedLanguageMap[r])
					.Distinct()
					.ToArray()
				: Array.Empty<Language.LanguageCodes>();

		[Pure]
		public static Regions GetPrimaryRegion(this Regions region)
		{
			if (((int) region & ((int) region - 1)) == 0)
				return region;
			if (region.HasFlag(Regions.USA))
				return Regions.USA;
			if (region.HasFlag(Regions.Japan))
				return Regions.Japan;
			if (region.HasFlag(Regions.Europe))
				return Regions.Europe;
			if (region.HasFlag(Regions.Australia))
				return Regions.Australia;
			if (region.HasFlag(Regions.Korea))
				return Regions.Korea;
			if (region.HasFlag(Regions.China))
				return Regions.China;
			if (region.HasFlag(Regions.Germany))
				return Regions.Germany;
			if (region.HasFlag(Regions.France))
				return Regions.France;
			if (region.HasFlag(Regions.Italy))
				return Regions.Italy;
			if (region.HasFlag(Regions.Spain))
				return Regions.Spain;
			if (region.HasFlag(Regions.Netherlands))
				return Regions.Netherlands;
			if (region.HasFlag(Regions.World))
				return Regions.World;
			return Regions.Unknown;
		}
	}
}
