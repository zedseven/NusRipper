using System;
using System.Collections.Concurrent;
using System.Threading;

namespace NusRipper
{
	public static class NonBlockingConsole
	{
		private static readonly BlockingCollection<string> messageQueue = new BlockingCollection<string>();

		static NonBlockingConsole()
		{
			Thread thread = new Thread(() => { while (true) Console.WriteLine(messageQueue.Take()); })
				{ IsBackground = true };
			thread.Start();
		}

		public static void WriteLine(string message)
			=> messageQueue.Add(message);
	}
}
