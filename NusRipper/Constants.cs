using System.Reflection;
using System.Text.RegularExpressions;

namespace NusRipper
{
	internal static class Constants
	{
		public static readonly string ProgramVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString();

		public static readonly Regex MetadataFileRegex = new Regex(Ripper.MetadataFileName + @"(?:\.\d+)?$");
		public static readonly Regex ContentEncryptedFileRegex = new Regex(@"[\da-fA-F]{8}$");
		public static readonly Regex ContentDecryptedFileRegex = new Regex(@"[\da-fA-F]{8}\.\w+$");
	}
}
