using System;

namespace EverythingSearchClient
{
	public class EverythingBusyException : Exception
	{
		public EverythingBusyException() : base("Everything service is busy") { }
	}
}
