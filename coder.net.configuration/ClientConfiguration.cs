using System;
using coder.net.core;

namespace coder.net.configuration
{
    public class ClientConfiguration : IClientConfiguration
    {
        public string Name { get; set; }

        public bool Enabled { get; set; }

        public string RemoteIpAddress { get; set; }

        public int RemotePort { get; set; }

        public bool EncryptConnection { get; set; }

        public string ServerName { get; set; }

        public string CertificateName { get; set; }

        public string QueueName { get; set; }
    }
}
