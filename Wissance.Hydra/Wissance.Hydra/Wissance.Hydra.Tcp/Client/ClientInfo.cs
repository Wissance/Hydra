using System.Net.Sockets;

namespace Wissance.Hydra.Tcp.Client
{
    public class ClientInfo
    {
        public Guid Id { get; set; }
        public Guid ChannelId { get; set; }                    // Tcp server channel (basically 1 channel = 1 port)
        public TcpClient Client { get; set; }
        public DateTimeOffset LastActivity { get; set; }       // Time of Last Activity
    }
}