using System;
using System.IO;
using System.Threading.Tasks;

namespace NusRipper
{
	public static class Images
	{
		private const int MagicSampleSize = 32;
		
		// Magic Bytes
		private static readonly byte[] GifMagic1 = { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61 }; // GIF87a
		private static readonly byte[] GifMagic2 = { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }; // GIF89a
		private static readonly byte[] BmpMagic  = { 0x42, 0x4D }; // BM
		private static readonly byte[] JpegMagic = { 0xFF, 0xD8, 0xFF }; // ÿØÿ
		private static readonly byte[] PngMagic  = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }; // .PNG....
		private static readonly byte[] ZipMagic  = { 0x50, 0x4B }; // PK

		public class UnknownFileTypeException : Exception
		{
			public UnknownFileTypeException() : base() {}
			public UnknownFileTypeException(string message) : base(message) {}
		}

		public static async Task<string> DetermineImageFileExtension(string path)
		{
			byte[] startingBytes;
			await using (FileStream fs = File.OpenRead(path))
			{
				startingBytes = new byte[MagicSampleSize];
				await fs.ReadAsync(startingBytes, 0, MagicSampleSize);
			}

			if (startingBytes.StartsWith(GifMagic1) || startingBytes.StartsWith(GifMagic2))
				return "gif";
			if (startingBytes.StartsWith(BmpMagic))
				return "bmp";
			if (startingBytes.StartsWith(JpegMagic))
				return "jpg";
			if (startingBytes.StartsWith(PngMagic))
				return "png";
			if (startingBytes.StartsWith(ZipMagic))
				return "zip";

			throw new UnknownFileTypeException($"Unknown file type for file '{path}'");
		}
	}
}
