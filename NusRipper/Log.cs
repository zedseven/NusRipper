using NLog;

namespace NusRipper
{
	internal static class Log
	{
		public static readonly Logger Instance = LogManager.GetCurrentClassLogger();
	}
}
