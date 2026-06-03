using System;
using System.Runtime.InteropServices;

namespace EverythingSearchClient
{

	/// <summary>
	/// Manages the detected IPC window of Everything.
	///
	/// HWnd is always resolved fresh via FindWindow on every call to IsAvailable,
	/// IsDbLoaded, and IsBusy. Caching the handle is unsafe: if Everything is killed
	/// and relaunched the old HWND becomes stale. A stale non-zero HWND makes
	/// IsAvailable return true while SendMessage to it silently returns 0, which
	/// causes the DB-loaded poll loop to spin forever instead of re-detecting.
	/// </summary>
	internal class IpcWindow
	{

		/// <summary>
		/// The last resolved window handle. May be stale — always call Detect() before use.
		/// </summary>
		public IntPtr HWnd { get; private set; } = IntPtr.Zero;

		public IpcWindow()
		{
			Detect();
		}

		public void Detect()
		{
			HWnd = FindWindow(EverythingIPC.EVERYTHING_IPC_WNDCLASS, null);
		}

		/// <summary>
		/// Always calls FindWindow fresh so a stale handle from a killed Everything
		/// process is never mistaken for a live one.
		/// </summary>
		public bool IsAvailable
		{
			get
			{
				Detect();
				return HWnd != IntPtr.Zero;
			}
		}

		public bool IsBusy()
		{
			uint b = SendMessage(HWnd, EverythingIPC.EVERYTHING_WM_IPC, EverythingIPC.EVERYTHING_IPC_IS_DB_BUSY, 0);
			return b != 0;
		}

		/// <summary>
		/// Returns true when the IPC window is up AND the database has finished loading.
		/// Sends EVERYTHING_IPC_IS_DB_LOADED (wParam 401), which maps directly to the
		/// old Everything_IsDBLoaded() DLL export. This is completely separate from
		/// EVERYTHING_IPC_IS_DB_BUSY (402), which only indicates an in-flight search query.
		/// </summary>
		public bool IsDbLoaded()
		{
			if (!IsAvailable)
				return false;
			uint result = SendMessage(HWnd, EverythingIPC.EVERYTHING_WM_IPC, EverythingIPC.EVERYTHING_IPC_IS_DB_LOADED, 0);
			return result != 0;
		}

		public Version GetVersion()
		{
			int ma = (int)SendMessage(HWnd, EverythingIPC.EVERYTHING_WM_IPC, EverythingIPC.EVERYTHING_IPC_GET_MAJOR_VERSION, 0);
			int mi = (int)SendMessage(HWnd, EverythingIPC.EVERYTHING_WM_IPC, EverythingIPC.EVERYTHING_IPC_GET_MINOR_VERSION, 0);
			int re = (int)SendMessage(HWnd, EverythingIPC.EVERYTHING_WM_IPC, EverythingIPC.EVERYTHING_IPC_GET_REVISION, 0);
			int bu = (int)SendMessage(HWnd, EverythingIPC.EVERYTHING_WM_IPC, EverythingIPC.EVERYTHING_IPC_GET_BUILD_NUMBER, 0);
			return new Version(ma, mi, re, bu);
		}

		[DllImport("user32.dll", SetLastError = true)]
		private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		private static extern uint SendMessage(IntPtr hWnd, uint Msg, uint wParam, uint lParam);

	}

}
