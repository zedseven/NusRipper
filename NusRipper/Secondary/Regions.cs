using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;

namespace NusRipper
{
	internal static class Regions
	{
		// ReSharper disable once InconsistentNaming
		[Flags]
		public enum Region
		{
			Unknown     = 0,
			USA         = 1,
			Japan       = 1 << 1,
			Europe      = 1 << 2,
			Australia   = 1 << 3,
			Korea       = 1 << 4,
			China       = 1 << 5,
			Germany     = 1 << 6,
			France      = 1 << 7,
			Italy       = 1 << 8,
			Spain       = 1 << 9,
			Netherlands = 1 << 10,
			World       = 1 << 11
		}

		// https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2
		public static readonly Dictionary<Region, string> RegionTwoLetterCodes = new Dictionary<Region, string>
		{
			[Region.USA]         = "US",
			[Region.Japan]       = "JP",
			[Region.Europe]      = "GB", // I know this isn't correct, but Nintendo has no Great Britain region - Europe is the closest.
			[Region.Australia]   = "AU",
			[Region.Korea]       = "KR",
			[Region.China]       = "CN",
			[Region.Germany]     = "DE",
			[Region.France]      = "FR",
			[Region.Italy]       = "IT",
			[Region.Spain]       = "ES",
			[Region.Netherlands] = "NL"
		};
		public static readonly Dictionary<Region, Languages.LanguageCode[]> RegionExpectedLanguageMap = new Dictionary<Region, Languages.LanguageCode[]>
		{
			[Region.USA]         = new[] { Languages.English, Languages.Japanese, Languages.Spanish },
			[Region.Japan]       = new[] { Languages.Japanese, Languages.English },
			[Region.Europe]      = new[] { Languages.English, Languages.French, Languages.German, Languages.Italian },
			[Region.Australia]   = new[] { Languages.English, Languages.Japanese },
			[Region.Korea]       = new[] { Languages.Korean, Languages.Japanese, Languages.English },
			[Region.China]       = new[] { Languages.Chinese, Languages.Japanese, Languages.English },
			[Region.Germany]     = new[] { Languages.German, Languages.Japanese },
			[Region.France]      = new[] { Languages.French, Languages.English, Languages.Japanese },
			[Region.Italy]       = new[] { Languages.Italian, Languages.English },
			[Region.Spain]       = new[] { Languages.Spanish },
			[Region.Netherlands] = new[] { Languages.German },
			[Region.World]       = new[] { Languages.English, Languages.Japanese }
		};
		public static readonly Dictionary<Region, Region[]> RegionRelatedRegionsMap = new Dictionary<Region, Region[]>
		{
			[Region.USA]         = new[] { Region.Europe },
			[Region.Japan]       = new[] { Region.Europe, Region.USA },
			[Region.Europe]      = new[] { Region.France, Region.Italy, Region.Germany, Region.Netherlands, Region.Spain, Region.USA }, // You may be wondering why the USA is here - apparently "Ivy the Kiwi? Mini", even though it's a Europe title, has only a USA eShop storefront
			[Region.Australia]   = new[] { Region.Europe },
			[Region.Korea]       = new[] { Region.Japan, Region.China },
			[Region.China]       = new[] { Region.Japan, Region.Korea },
			[Region.Germany]     = new[] { Region.Netherlands, Region.Europe },
			[Region.France]      = new[] { Region.Europe },
			[Region.Italy]       = new[] { Region.Europe },
			[Region.Spain]       = new[] { Region.Europe },
			[Region.Netherlands] = new[] { Region.Germany, Region.Europe },
			[Region.World]       = new[] { Region.Europe, Region.USA }
		};

		[Pure]
		public static IEnumerable<Region> DecomposeRegion(this Region region)
			=> Enum.GetValues(typeof(Region))
				.Cast<Region>()
				.Where(r => r != Region.Unknown && region.HasFlag(r));

		[Pure]
		public static Languages.LanguageCode[] GetExpectedLanguages(Region region)
			=> region != Region.Unknown
				? DecomposeRegion(region)
					.SelectMany(r => RegionExpectedLanguageMap[r])
					.Distinct()
					.ToArray()
				: Array.Empty<Languages.LanguageCode>();

		[Pure]
		public static Region GetPrimaryRegion(this Region region)
		{
			if (((int) region & ((int) region - 1)) == 0)
				return region;
			if (region.HasFlag(Region.USA))
				return Region.USA;
			if (region.HasFlag(Region.Japan))
				return Region.Japan;
			if (region.HasFlag(Region.Europe))
				return Region.Europe;
			if (region.HasFlag(Region.Australia))
				return Region.Australia;
			if (region.HasFlag(Region.Korea))
				return Region.Korea;
			if (region.HasFlag(Region.China))
				return Region.China;
			if (region.HasFlag(Region.Germany))
				return Region.Germany;
			if (region.HasFlag(Region.France))
				return Region.France;
			if (region.HasFlag(Region.Italy))
				return Region.Italy;
			if (region.HasFlag(Region.Spain))
				return Region.Spain;
			if (region.HasFlag(Region.Netherlands))
				return Region.Netherlands;
			if (region.HasFlag(Region.World))
				return Region.World;
			return Region.Unknown;
		}
	}
}
