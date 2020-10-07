using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;

namespace NusRipper
{
	internal static class Regions
	{
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

		public static readonly Dictionary<Region, Languages.LanguageCode[]> RegionExpectedLanguageMap = new Dictionary<Region, Languages.LanguageCode[]>();

		static Regions()
		{
			RegionExpectedLanguageMap[Region.USA]         = new[] { Languages.English, Languages.Japanese, Languages.Spanish };
			RegionExpectedLanguageMap[Region.Japan]       = new[] { Languages.Japanese, Languages.English };
			RegionExpectedLanguageMap[Region.Europe]      = new[] { Languages.English, Languages.French, Languages.German, Languages.Italian };
			RegionExpectedLanguageMap[Region.Australia]   = new[] { Languages.English, Languages.Japanese };
			RegionExpectedLanguageMap[Region.Korea]       = new[] { Languages.Korean, Languages.Japanese, Languages.English };
			RegionExpectedLanguageMap[Region.China]       = new[] { Languages.Chinese, Languages.Japanese, Languages.English };
			RegionExpectedLanguageMap[Region.Germany]     = new[] { Languages.German, Languages.Japanese };
			RegionExpectedLanguageMap[Region.France]      = new[] { Languages.French, Languages.English, Languages.Japanese };
			RegionExpectedLanguageMap[Region.Italy]       = new[] { Languages.Italian, Languages.English };
			RegionExpectedLanguageMap[Region.Spain]       = new[] { Languages.Spanish };
			RegionExpectedLanguageMap[Region.Netherlands] = new[] { Languages.German };
			RegionExpectedLanguageMap[Region.World]       = new[] { Languages.English, Languages.Japanese };
		}

		[Pure]
		public static Languages.LanguageCode[] GetExpectedLanguages(Region region)
			=> region != Region.Unknown
				? Enum.GetValues(region.GetType())
					.Cast<Region>()
					.Where(r => r != Region.Unknown && region.HasFlag(r))
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

		[Pure]
		public static string ToPrettyString(this Region region)
			=> string.Join(", ", Enum.GetValues(region.GetType())
				.Cast<Region>()
				.Where(r => region != Region.Unknown && region.HasFlag(r))
				.Select(r => r.ToString()));
	}
}
