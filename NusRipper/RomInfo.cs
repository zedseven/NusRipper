﻿using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NusRipper
{
	public class RomInfo
	{
		private const int GameTitleOffset = 0x00000000;
		private const int GameTitleSize = 12;
		private const int GameCodeOffset = 0x0000000C;
		private const int GameCodeSize = 4;
		private const int TitleInfoAddressOffset = 0x00000068;
		private const int TitleInfoAddressSize = 4;
		private const int TitleInfoTitlesOffset = 0x00000240;
		private const int TitleInfoTitleSize = 0x100;
		private const int TitleInfoStaticIconOffset = 0x00000020;
		private const int TitleInfoAnimIconBitmapsOffset = 0x00001240;
		private const int TitleInfoAnimIconBitmapCount = 8;
		private const int TitleInfoAnimIconPalettesOffset = 0x00002240;
		private const int TitleInfoAnimIconPaletteCount = 8;
		private const int TitleInfoAnimIconSequenceOffset = 0x00002340;
		private const int TitleInfoAnimIconSequenceFrameCount = 64;
		private const int HeaderChecksumOffset = 0x0000015E;

		private const int IconTotalSize = 0x220;
		private const int IconBitmapSize = 0x200;
		private const int IconPaletteColours = 16;
		private const int IconPaletteBytesPerColour = 2;
		private const int IconSquareDim = 32;
		private const float ColourIntensityConversion = 255f / 31f;
		private const float FrameDurationConversion = 1f / 60f * 100f;

		public enum TitleInfoVersions
		{
			Original = 0x0001,
			ChineseTitle = 0x0002,
			ChineseKoreanTitles = 0x0003,
			ChineseKoreanTitlesAnimated = 0x0103
		}

		public readonly TitleInfoVersions TitleInfoVersion;

		public readonly string GameTitle;
		public readonly string GameCode;

		public readonly string[] Titles = new string[8];

		public enum TitleIndices
		{
			JapaneseIndex = 0,
			EnglishIndex = 1,
			FrenchIndex = 2,
			GermanIndex = 3,
			ItalianIndex = 4,
			SpanishIndex = 5,
			ChineseIndex = 6,
			KoreanIndex = 7
		}

		public readonly bool ValidContent;

		public readonly Image<Rgba32> StaticIcon;
		public readonly Image<Rgba32> AnimatedIcon;

		public RomInfo(string decPath, bool parseImages = false)
		{
			byte[] contentBytes = File.ReadAllBytes(decPath);

			ushort headerChecksum = CalcCrc16Modbus(contentBytes.Slice(0, HeaderChecksumOffset));
			ushort romChecksumValue = BitConverter.ToUInt16(contentBytes.Slice(HeaderChecksumOffset, 2, true).Reverse().ToArray());
			ValidContent = headerChecksum == romChecksumValue;
			if (!ValidContent)
				return;

			GameTitle = new string(contentBytes.Slice(GameTitleOffset, GameTitleSize).Select(b => (char) b).ToArray()).TrimEnd('\0');
			GameCode = new string(contentBytes.Slice(GameCodeOffset, GameCodeSize).Select(b => (char) b).ToArray());

			int titleInfoAddress = BitConverter.ToInt32(contentBytes.Slice(TitleInfoAddressOffset, TitleInfoAddressSize, true).Reverse().ToArray());

			if (titleInfoAddress == 0x00000000)
				return;

			TitleInfoVersion = (TitleInfoVersions)BitConverter.ToInt16(contentBytes.Slice(titleInfoAddress, 2, true).Reverse().ToArray());

			UnicodeEncoding encoding = new UnicodeEncoding(false, false);
			int titleCount = TitleInfoVersion == TitleInfoVersions.Original ? 6 : TitleInfoVersion == TitleInfoVersions.ChineseTitle ? 7 : 8;
			for (int i = 0; i < titleCount; i++)
				Titles[i] = encoding.GetString(contentBytes.Slice(titleInfoAddress + TitleInfoTitlesOffset + TitleInfoTitleSize * i, TitleInfoTitleSize)).Trim('\0', '\uffff').AsNullIfEmpty();

			if (!parseImages)
				return;

			// Static icon
			StaticIcon = BuildImage(ParseBitmapIndices(contentBytes.Slice(titleInfoAddress + TitleInfoStaticIconOffset, IconBitmapSize)),
				ParsePalette(contentBytes.Slice(titleInfoAddress + TitleInfoStaticIconOffset + IconBitmapSize, IconPaletteBytesPerColour * IconPaletteColours)));

			PngMetadata pngMetadata = StaticIcon.Metadata.GetFormatMetadata(PngFormat.Instance);
			pngMetadata.ColorType = PngColorType.Palette;
			pngMetadata.TextData = new List<PngTextData> { new PngTextData("description", $"Icon for the DSiWare title \"{GetFriendlyTitle()}\" (game code \"{GameCode}\").", "en-ca", "") };

			// Animated icon
			if (TitleInfoVersion != TitleInfoVersions.ChineseKoreanTitlesAnimated)
				return;

			byte[] sequenceBytes = contentBytes.Slice(titleInfoAddress + TitleInfoAnimIconSequenceOffset, TitleInfoAnimIconSequenceFrameCount * 2);

			// Non-animated icons start with 0x01000001
			if (sequenceBytes[0] == 0x01 && sequenceBytes[1] == 0x00 && sequenceBytes[2] == 0x00 && sequenceBytes[3] == 0x01)
				return;

			byte[][] bitmapPaletteIndices = new byte[TitleInfoAnimIconBitmapCount][];
			for (int i = 0; i < TitleInfoAnimIconBitmapCount; i++)
				bitmapPaletteIndices[i] = ParseBitmapIndices(contentBytes.Slice(
					titleInfoAddress + TitleInfoAnimIconBitmapsOffset + i * IconBitmapSize,
					IconBitmapSize));

			Rgba32[][] paletteColours = new Rgba32[TitleInfoAnimIconPaletteCount][];
			for (int i = 0; i < TitleInfoAnimIconPaletteCount; i++)
				paletteColours[i] = ParsePalette(contentBytes.Slice(
					titleInfoAddress + TitleInfoAnimIconPalettesOffset + i * IconPaletteBytesPerColour * IconPaletteColours,
					IconPaletteBytesPerColour * IconPaletteColours));

			AnimatedIcon = new Image<Rgba32>(IconSquareDim, IconSquareDim);
			for (int i = 0; i < TitleInfoAnimIconSequenceFrameCount; i++)
			{
				if (sequenceBytes[i * 2] == 0x00 && sequenceBytes[i * 2 + 1] == 0x00)
					break;

				bool flipVertical   =         (sequenceBytes[i * 2 + 1] & 0b10000000) >> 7 == 1;
				bool flipHorizontal =         (sequenceBytes[i * 2 + 1] & 0b01000000) >> 6 == 1;
				byte paletteIndex   = (byte) ((sequenceBytes[i * 2 + 1] & 0b00111000) >> 3);
				byte bitmapIndex    = (byte)  (sequenceBytes[i * 2 + 1] & 0b00000111);
				byte frameDuration  = sequenceBytes[i * 2]; // in 60Hz units

				Image<Rgba32> frame = BuildImage(bitmapPaletteIndices[bitmapIndex], paletteColours[paletteIndex], flipHorizontal, flipVertical);

				ImageFrame<Rgba32> aFrame = AnimatedIcon.Frames.AddFrame(frame.Frames.RootFrame);
				aFrame.Metadata.GetFormatMetadata(GifFormat.Instance).FrameDelay = (int) Math.Round(frameDuration * FrameDurationConversion);
			}
			AnimatedIcon.Frames.RemoveFrame(0);
			AnimatedIcon.Metadata.GetFormatMetadata(GifFormat.Instance).RepeatCount = 0;
			GifMetadata gifMetadata = AnimatedIcon.Metadata.GetFormatMetadata(GifFormat.Instance);
			gifMetadata.RepeatCount = 0;
			gifMetadata.Comments = new List<string> { $"Icon for the DSiWare title \"{GetFriendlyTitle()}\" (game code \"{GameCode}\")." };
		}

		private byte[] ParseBitmapIndices(byte[] bitmapBytes)
		{
			byte[] bitmapPaletteIndices = new byte[IconSquareDim * IconSquareDim];
			for (int i = 0; i < IconBitmapSize; i++)
			{
				bitmapPaletteIndices[i * 2]     = (byte)  (bitmapBytes[i] & 0b00001111);
				bitmapPaletteIndices[i * 2 + 1] = (byte) ((bitmapBytes[i] & 0b11110000) >> 4);
			}

			return bitmapPaletteIndices;
		}

		private Rgba32[] ParsePalette(byte[] paletteBytes)
		{
			Rgba32[] paletteColours = new Rgba32[IconPaletteColours];
			paletteColours[0] = new Rgba32(0, 0, 0, 0);
			for (int i = 1; i < IconPaletteColours; i++)
			{
				ushort colour = BitConverter.ToUInt16(paletteBytes.Slice(i * IconPaletteBytesPerColour, IconPaletteBytesPerColour, true).Reverse().ToArray());
				byte redIntensity   = (byte)  (colour & 0b0000000000011111);
				byte greenIntensity = (byte) ((colour & 0b0000001111100000) >> 5);
				byte blueIntensity  = (byte) ((colour & 0b0111110000000000) >> 10);
				paletteColours[i] = new Rgba32(
					(byte) Math.Round(redIntensity   * ColourIntensityConversion),
					(byte) Math.Round(greenIntensity * ColourIntensityConversion),
					(byte) Math.Round(blueIntensity  * ColourIntensityConversion));
			}

			return paletteColours;
		}

		private Image<TPixel> BuildImage<TPixel>(byte[] bitmapPaletteIndices, TPixel[] palette, bool flipHorizontal = false, bool flipVertical = false)
			where TPixel : unmanaged, IPixel<TPixel>
		{
			Image<TPixel> outImage = new Image<TPixel>(IconSquareDim, IconSquareDim);
			{
				int i = 0;
				for (int ty = 0; ty < 4; ty++)
				for (int tx = 0; tx < 4; tx++)
				for (int y = 0;   y < 8; y++)
				for (int x = 0;   x < 8; x++)
				{
					int tmp0 = (y + ty * 8) * 32;
					int tmp1 = x + tx * 8;
					int offs = tmp0 + tmp1;
					outImage[offs % IconSquareDim, offs / IconSquareDim] = palette[bitmapPaletteIndices[i]];
					i++;
				}
			}

			if (flipHorizontal)
				outImage.Mutate(x => x.Flip(FlipMode.Horizontal));
			if (flipVertical)
				outImage.Mutate(x => x.Flip(FlipMode.Vertical));

			return outImage;
		}

		public string GetProperName(bool withGameCode = false)
		{
			if (withGameCode)
			{
				if (Titles[(int) TitleIndices.EnglishIndex] != null)
					return $"{GameCode} - {GameTitle} - {GetFriendlyTitle()}";
				if (GameTitle != null)
					return $"{GameCode} - {GameTitle}";
				return GameCode;
			}
			if (Titles[(int) TitleIndices.EnglishIndex] != null)
				return $"{GameTitle} - {GetFriendlyTitle()}";
			return GameTitle;
		}

		public string GetFriendlyTitle()
			=> Titles[(int) TitleIndices.EnglishIndex] != null
				? string.Join(' ', Titles[(int) TitleIndices.EnglishIndex].Split('\n').Select(l => l.Trim(' ', '\0')).ToArray())
				: null;

		private static ushort CalcCrc16Modbus(byte[] bytes)
		{
			ushort crc = 0xFFFF;

			for (int pos = 0; pos < bytes.Length; pos++)
			{
				crc ^= bytes[pos];

				for (int i = 8; i != 0; i--)
				{
					if ((crc & 0x0001) != 0)
					{
						crc >>= 1;
						crc ^= 0xA001;
					}
					else
						crc >>= 1;
				}
			}
			return crc;
		}
	}
}