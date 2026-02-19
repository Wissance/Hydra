using System;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Wissance.Hydra.Tcp.Transport
{
    public class TcpChannel
    {
        public bool Status { get; set; }
        public TcpListener Listener { get; set; }
        //public Task ChannelProcessor { get; set; }
        public Thread ChannelProcessor { get; set; }
        public CancellationToken Cancellation { get; set; }
        public X509Certificate Certificate { get; set; }
    }
}