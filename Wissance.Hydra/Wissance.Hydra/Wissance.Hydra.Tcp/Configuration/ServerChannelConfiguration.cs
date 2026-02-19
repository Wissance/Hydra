using System;

namespace Wissance.Hydra.Tcp.Configuration
{
    public class ServerChannelConfiguration
    {
        public ServerChannelConfiguration()
        {
            ChannelId = Guid.NewGuid();
        }

        public Guid ChannelId { get; }
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public bool IsSecure { get; set; }
        public string CertificatePath { get; set; }
    }
}