using System;

namespace EverythingSearchClient
{

	/// <summary>
	/// Read-only results of a search run
	/// </summary>
	public class Result
	{

		[Flags]
		public enum ItemFlags
		{
			None = 0,
			Folder = 1,
			Drive = 2,
			Unknown = 0x80
		}

		[Flags]
		public enum ItemFileAttributes
		{
			None = 0,
			ReadOnly = 1,
			Hidden = 2,
			System = 4,
			Directory = 16,
			Archive = 32,
			Normal = 128
		}

		public class Item
		{
			public ItemFlags Flags { get; protected set; }
			public string Name { get; protected set; }
			public string Path { get; protected set; }
			public ulong? Size { get; protected set; }
			public DateTime? CreationTime { get; protected set; }
			public DateTime? LastWriteTime { get; protected set; }
			public ItemFileAttributes? FileAttributes { get; protected set; }
		}

		public UInt32 TotalItems { get; protected set; }
		public UInt32 NumItems { get { return (UInt32)Items.Length; } }
		public UInt32 Offset { get; protected set; }
		public Item[] Items { get; protected set; }

		protected Result()
		{
			Items = new Item[0];
		}
	}

}
