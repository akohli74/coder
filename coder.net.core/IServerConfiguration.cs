using System;
namespace coder.net.core
{
    public interface IServerConfiguration
    {
		string Name { get; set; }

		bool Enabled { get; set; }

		string IpAddress { get; set; }

		int Port { get; set; }

		short ConnectionCount { get; set; }

		bool ListenerDelay { get; set; }

		int DelayDuration { get; set; }

		bool EncryptConnection { get; set; }

		string X509CertificatePath { get; set; }

		string QueueName { get; set; }

		bool KeepConnectionOpen { get; set; }

		bool ShutdownClientOnOpenSocket { get; set; }

		int ReadTimeout { get; set; }
	}
}
