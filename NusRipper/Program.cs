using System.Net.Http;
using System.Threading.Tasks;

namespace NusRipper
{
	public sealed class Program
	{
		private static async Task Main(string[] args)
		{
			string testPath = @"Y:\Emus\DSiWare\NUS Backup\Dropzone";

			HttpClient client = new HttpClient();
			//Ripper.DownloadTitleFile(client, testPath, "00030005484E4441", "tmd");
			await Ripper.DownloadTitle(client, testPath, "000300044B4E344A", 3);
			await Ripper.DownloadTitle(client, testPath, "000300044B4D5245", 3, 256);
			await Ripper.DownloadTitle(client, testPath, "00030004484E474A", 0, 256, 768);
		}
	}
}
