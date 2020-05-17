using System;
using coder.net.core;

namespace coder.net.configuration
{
    public class ServerConfiguration : IServerConfiguration
    {
		public string Name { get; set; }

		public bool Enabled { get; set; }

		public string IpAddress { get; set; }

		public int Port { get; set; }

		public short ConnectionCount { get; set; }

		public bool ListenerDelay { get; set; }

		public int DelayDuration { get; set; }

		public bool EncryptConnection { get; set; }

		public string X509CertificatePath { get; set; }

		public string QueueName { get; set; }

		public bool KeepConnectionOpen { get; set; }

		public bool ShutdownClientOnOpenSocket { get; set; }

		public int ReadTimeout { get; set; }
	}
}
