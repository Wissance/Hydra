using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Wissance.Hydra.Common.Data;
using Wissance.Hydra.Tcp.Client;

namespace Wissance.Hydra.Tcp.Transport
{
    /// <summary>
    ///    This interface implements transport logic in operations between other systems/subsystems && Client via TCP
    ///    Channel == Port.
    /// </summary>
    public interface ITcpServer : IDisposable
    {
        // Server management methods: Start, Stop, Restart
        // 1. Methods that implements operation over all channels
        Task<OperationResult> StartAsync();
        Task<OperationResult> StopAsync();
        Task<OperationResult> RestartAsync();
        bool IsReady();
        void DropAllConnectedClients();
        // 3. Server Stats methods: Get clients and so on
        IList<ClientInfo> GetClientsStats { get; set; }
        // 4. Packets handler functions
        // 4.1 Handle Packets handlers assignment
        void AssignHandler(Func<byte[], ClientInfo, Task<byte[]>> handler);
        // 4.2 Handler Connections assignment
        void AssignConnectionHandler(Action<ClientInfo> handler);
        // 5. Direct Exchange function: send data & so on
        Task<OperationResult> SendDataAsync(Guid clientId, Byte[] data);
    }
}