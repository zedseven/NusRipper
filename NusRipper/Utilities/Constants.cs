using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace NusRipper
{
	internal static class Constants
	{
		private static readonly Assembly ExecutingAssembly = Assembly.GetExecutingAssembly();
		public static readonly string AssemblyPath         = Path.GetDirectoryName(ExecutingAssembly.Location);
		public static readonly string ProgramVersion       = ExecutingAssembly.GetName().Version?.ToString();

		public static readonly string ReferenceFilesPath = Path.Combine(AssemblyPath, "ReferenceFiles");

		public static readonly Regex MetadataFileRegex         = new Regex(Ripper.MetadataFileName + @"(?:\.\d+)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		public static readonly Regex MetadataIdFileRegex       = new Regex(@"fffe[\da-f]{4}$",                       RegexOptions.Compiled | RegexOptions.IgnoreCase);
		public static readonly Regex ContentEncryptedFileRegex = new Regex(@"[\da-e][\da-f]{7}$",                    RegexOptions.Compiled | RegexOptions.IgnoreCase);
		public static readonly Regex ContentDecryptedFileRegex = new Regex(@"[\da-f]{8}\.\w+$",                      RegexOptions.Compiled | RegexOptions.IgnoreCase);
		public static readonly Regex MiscellaneousFileRegex    = new Regex(@"[\df][\da-f]{7}$",                      RegexOptions.Compiled | RegexOptions.IgnoreCase);
	}
}
