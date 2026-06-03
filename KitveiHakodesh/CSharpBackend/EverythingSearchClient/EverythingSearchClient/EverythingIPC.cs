using System;
using System.Runtime.InteropServices;

namespace EverythingSearchClient
{

	/// <summary>
	/// This class is based on everything_ipc.h from the Everything SDK
	/// </summary>
	internal class EverythingIPC
	{

		internal const string EVERYTHING_IPC_WNDCLASS = "EVERYTHING_TASKBAR_NOTIFICATION";
		internal const uint EVERYTHING_WM_IPC = 0x0400; // WM_USER
		internal const uint EVERYTHING_IPC_GET_MAJOR_VERSION = 0;
		internal const uint EVERYTHING_IPC_GET_MINOR_VERSION = 1;
		internal const uint EVERYTHING_IPC_GET_REVISION = 2;
		internal const uint EVERYTHING_IPC_GET_BUILD_NUMBER = 3;
		internal const uint EVERYTHING_IPC_IS_DB_LOADED = 401;  // Everything_IsDBLoaded() — true once the initial index build is complete
		internal const uint EVERYTHING_IPC_IS_DB_BUSY = 402;    // Everything_IsDBBusy()   — true while processing a search query

		[StructLayout(LayoutKind.Sequential)]
		internal struct EVERYTHING_IPC_QUERY
		{
			public UInt32 reply_hwnd;
			public UInt32 reply_copydata_message;
			public UInt32 search_flags;
			public UInt32 offset;
			public UInt32 max_results;
			// followed by null-terminated wide string
		}

		internal const uint EVERYTHING_IPC_MATCHCASE = 0x00000001;
		internal const uint EVERYTHING_IPC_MATCHWHOLEWORD = 0x00000002;
		internal const uint EVERYTHING_IPC_MATCHPATH = 0x00000004;
		internal const uint EVERYTHING_IPC_REGEX = 0x00000008;
		internal const uint EVERYTHING_IPC_COPYDATAQUERYW = 2;
		internal const uint EVERYTHING_IPC_COPYDATA_QUERY2W = 18;
		internal const uint EVERYTHING_IPC_FOLDER = 0x00000001;
		internal const uint EVERYTHING_IPC_DRIVE = 0x00000002;

		[StructLayout(LayoutKind.Sequential)]
		internal struct EVERYTHING_IPC_QUERY2
		{
			public UInt32 reply_hwnd;
			public UInt32 reply_copydata_message;
			public UInt32 search_flags;
			public UInt32 offset;
			public UInt32 max_results;
			public UInt32 request_flags;
			public UInt32 sort_type;
			// followed by null-terminated search string
		}

		internal const uint EVERYTHING_IPC_QUERY2_REQUEST_NAME = 0x00000001;
		internal const uint EVERYTHING_IPC_QUERY2_REQUEST_PATH = 0x00000002;
		internal const uint EVERYTHING_IPC_QUERY2_REQUEST_FULL_PATH_AND_NAME = 0x00000004;
		internal const uint EVERYTHING_IPC_QUERY2_REQUEST_EXTENSION = 0x00000008;
		internal const uint EVERYTHING_IPC_QUERY2_REQUEST_SIZE = 0x00000010;
		internal const uint EVERYTHING_IPC_QUERY2_REQUEST_DATE_CREATED = 0x00000020;
		internal const uint EVERYTHING_IPC_QUERY2_REQUEST_DATE_MODIFIED = 0x00000040;
		internal const uint EVERYTHING_IPC_QUERY2_REQUEST_DATE_ACCESSED = 0x00000080;
		internal const uint EVERYTHING_IPC_QUERY2_REQUEST_ATTRIBUTES = 0x00000100;

		internal const uint EVERYTHING_IPC_SORT_NAME_ASCENDING = 1;
		internal const uint EVERYTHING_IPC_SORT_NAME_DESCENDING = 2;
		internal const uint EVERYTHING_IPC_SORT_PATH_ASCENDING = 3;
		internal const uint EVERYTHING_IPC_SORT_PATH_DESCENDING = 4;
		internal const uint EVERYTHING_IPC_SORT_SIZE_ASCENDING = 5;
		internal const uint EVERYTHING_IPC_SORT_SIZE_DESCENDING = 6;
		internal const uint EVERYTHING_IPC_SORT_EXTENSION_ASCENDING = 7;
		internal const uint EVERYTHING_IPC_SORT_EXTENSION_DESCENDING = 8;
		internal const uint EVERYTHING_IPC_SORT_DATE_CREATED_ASCENDING = 11;
		internal const uint EVERYTHING_IPC_SORT_DATE_CREATED_DESCENDING = 12;
		internal const uint EVERYTHING_IPC_SORT_DATE_MODIFIED_ASCENDING = 13;
		internal const uint EVERYTHING_IPC_SORT_DATE_MODIFIED_DESCENDING = 14;
	}

}
