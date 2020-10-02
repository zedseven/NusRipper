using System;
using System.Collections.Generic;
using System.IO;

namespace NusRipper
{
	public class TitleMetadata
	{
		private const int NumContentsOffset = 0x000001DE;
		private const int ContentsListOffset = 0x000001E4;
		private const int BytesPerContentChunk = 36;

		public class TitleContentInfo
		{
			public readonly uint Id;
			public readonly ushort Index;
			public readonly ulong FileSize;
			public readonly byte[] DecSha1Hash;

			public TitleContentInfo(uint id, ushort index, ulong fileSize, byte[] decSha1Hash)
			{
				Id = id;
				Index = index;
				FileSize = fileSize;
				DecSha1Hash = decSha1Hash;
			}
		}

		public readonly string FileName;
		public readonly short NumContents;
		public readonly TitleContentInfo[] ContentInfo;

		public TitleMetadata(string fileName, short numContents, TitleContentInfo[] contentInfo)
		{
			FileName = fileName;
			NumContents = numContents;
			ContentInfo = contentInfo;
		}

		public TitleMetadata(string metadataPath)
		{
			FileName = Path.GetFileName(metadataPath);

			byte[] bytes = File.ReadAllBytes(metadataPath);

			NumContents = BitConverter.ToInt16(bytes.Slice(NumContentsOffset, 2, true));
			List<TitleContentInfo> contentInfo = new List<TitleContentInfo>();
			for (int i = 0; i < NumContents; i++)
				contentInfo.Add(new TitleContentInfo(
					BitConverter.ToUInt32(bytes.Slice(ContentsListOffset + i * BytesPerContentChunk, 4, true)),
					BitConverter.ToUInt16(bytes.Slice(ContentsListOffset + i * BytesPerContentChunk + 4, 2, true)),
					BitConverter.ToUInt64(bytes.Slice(ContentsListOffset + i * BytesPerContentChunk + 8, 8, true)),
					bytes.Slice(ContentsListOffset + i * BytesPerContentChunk + 16, 20)));

			ContentInfo = contentInfo.ToArray();
		}
	}
}
