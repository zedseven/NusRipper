using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Xml;

namespace NusRipper
{
	internal static class Port3dsInfo
	{
		private static readonly Dictionary<string, string> DsiTo3dsTitleIds = new Dictionary<string, string>();
		private static readonly X509Certificate2Collection Certificates;

		static Port3dsInfo()
		{
			// Load DSi -> 3DS Title ID Map
			Helpers.LoadCsvIntoDictionary(Path.Combine(Constants.ReferenceFilesPath, "DSi23DS.csv"), DsiTo3dsTitleIds);

			// Load Certificates
			X509Store keystore = new X509Store(StoreName.My, StoreLocation.LocalMachine);
			keystore.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
			Certificates = keystore.Certificates.Find(X509FindType.FindBySubjectName, "CTR Common Prod 1", false);
			if (Certificates.Count <= 0)
				Log.Instance.Warn("No certificates could be found for the CTR on this machine. 3DS eShop info will likely be unavailable.");
		}

		public static bool Has3dsPort(string titleIdDsi)
			=> DsiTo3dsTitleIds.ContainsKey(titleIdDsi);

		public static async Task<Language.LanguageCodes[]> GetTitle3dsInfo(string titleIdDsi, string twoLetterRegion)
		{
			if (string.IsNullOrWhiteSpace(titleIdDsi) || !DsiTo3dsTitleIds.TryGetValue(titleIdDsi, out string titleId3ds))
				return null;

			// Get the EShop content ID for a given title
			string ninjaUrl = $"https://ninja.ctr.shop.nintendo.net/ninja/ws/titles/id_pair?title_id[]={titleId3ds}";

			HttpWebRequest ninjaRequest = WebRequest.CreateHttp(ninjaUrl);
			ninjaRequest.Method = "get";
			ninjaRequest.ClientCertificates = Certificates;

			WebResponse ninjaResponse;
			try
			{
				ninjaResponse = ninjaRequest.GetResponse();
			}
			catch (WebException e)
			{
				Log.Instance.Error($"Title '{titleIdDsi}' ('{titleId3ds}' on 3DS) received an error when connecting to ninja at URL \"{ninjaUrl}\": {e.Message}");
				return null;
			}
			Stream ninjaResponseStream = ninjaResponse.GetResponseStream();

			if (ninjaResponseStream == null)
				return null;

			string eShopContentId = null;
			using (XmlReader ninjaReader = XmlReader.Create(ninjaResponseStream, new XmlReaderSettings { Async = true }))
			{
				while (await ninjaReader.ReadAsync())
				{
					if (ninjaReader.NodeType == XmlNodeType.Element && ninjaReader.Name == "ns_uid")
					{
						eShopContentId = ninjaReader.ReadElementContentAsString();
						break;
					}
				}
			}

			if (string.IsNullOrWhiteSpace(eShopContentId))
			{
				Log.Instance.Error($"Unable to get the eShop content ID for title '{titleIdDsi}' ('{titleId3ds}' on 3DS) using the URL \"{ninjaUrl}\".");
				return null;
			}

			// Use the content ID to get the EShop information
			string samuraiUrl = $"https://samurai.ctr.shop.nintendo.net/samurai/ws/{twoLetterRegion}/title/{eShopContentId}";

			HttpWebRequest samuraiRequest = WebRequest.CreateHttp(samuraiUrl);
			samuraiRequest.Method = "get";

			WebResponse samuraiResponse;
			try
			{
				samuraiResponse = samuraiRequest.GetResponse();
			}
			catch (WebException e)
			{
				Log.Instance.Trace($"Title '{titleIdDsi}' ('{titleId3ds}' on 3DS) received an error when connecting to samurai at URL \"{samuraiUrl}\": {e.Message}");
				return null;
			}
			Stream samuraiResponseStream = samuraiResponse.GetResponseStream();

			if (samuraiResponseStream == null)
				return null;

			string name = null;
			string formalName = null;
			List<Language.LanguageCodes> languages = new List<Language.LanguageCodes>();
			using (XmlReader samuraiReader = XmlReader.Create(samuraiResponseStream, new XmlReaderSettings { Async = true }))
			{
				while (await samuraiReader.ReadAsync())
				{
					if (samuraiReader.NodeType != XmlNodeType.Element || samuraiReader.Name != "languages")
						continue;
					// Keep going until the end tag of languages is reached
					while (samuraiReader.Name != "languages" || samuraiReader.NodeType != XmlNodeType.EndElement)
					{
						if (!await samuraiReader.ReadAsync())
							break;
						if (samuraiReader.NodeType != XmlNodeType.Element)
							continue;

						if (samuraiReader.Name == "iso_code")
							languages.Add(
								Language.IdentifierLanguageDict[
									samuraiReader.ReadElementContentAsString().ToLowerInvariant()]);
					}
				}
			}

			if (languages.Count <= 0)
				Log.Instance.Warn($"The title '{titleIdDsi}' ('{titleId3ds}' on 3DS) has no listed languages according to samurai at the following URL: \"{samuraiUrl}\".");

			return languages.Distinct().ToArray();
		}
	}
}
