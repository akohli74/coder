namespace coder.net.core
{
    public interface IClientConfiguration
    {
		string Name { get; set; }

		bool Enabled { get; set; }

		string RemoteIpAddress { get; set; }

		int RemotePort { get; set; }

		bool EncryptConnection { get; set; }

		string ServerName { get; set; }

		string CertificateName { get; set; }

		string QueueName { get; set; }
	}
}
