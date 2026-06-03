using System;

namespace EverythingSearchClient
{
	public class ResultsNotReceivedException : Exception
	{
		public ResultsNotReceivedException() : base("Failed to receive results") { }
	}
}
