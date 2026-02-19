using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Wissance.Hydra.Common.Data;
using Wissance.Hydra.Tcp.Client;
using Wissance.Hydra.Tcp.Configuration;
using Wissance.Hydra.Tcp.Errors;

namespace Wissance.Hydra.Tcp.Transport
{
    internal class TcpChannelContext
    {
        public TcpChannelContext(Guid id, TcpChannel channel)
        {
            ChannelId = id;
            Channel = channel;
        }

        public Guid ChannelId { get; set; }
        public TcpChannel Channel  { get; set; }
    }


    /// <summary>
    ///     This class is an impl of multichannel Tcp Server (actual multi ports)
    ///     To run TCP Server with Certificate we MUST create a .pfx certificate like this:
    ///         1. openssl genrsa -out server.key 2048
    ///         2. openssl req -new -x509 -sha256 -key server.key -out server.crt -days 3650
    ///         3. openssl pkcs12 -export -out certificate.pfx -inkey server.key -in server.crt
    /// </summary>
    public class MultiChannelTcpServer : ITcpServer
    {
        public MultiChannelTcpServer(ServerChannelConfiguration[] channelsCfg, ILoggerFactory loggerFactory,
            Action<ClientInfo> connectionHandler = null, Func<byte[], ClientInfo, Task<byte[]>> packetHandler = null,
            Action<ErrorType, Exception> errHandler = null)
        {
            _channelsCfg = channelsCfg;
            _connectionHandler = connectionHandler;
            _packetHandler = packetHandler;
            _errHandler = errHandler;
            // todo(umv): init server channels
            _clients = new ConcurrentDictionary<Guid, ClientInfo>();
            _serverChannels = new ConcurrentDictionary<Guid, TcpChannel>();
            _logger = loggerFactory.CreateLogger<MultiChannelTcpServer>();
        }

        public async Task<OperationResult> StartAsync()
        {
            try
            {
                _cancellationSource = new CancellationTokenSource();
                
                foreach (ServerChannelConfiguration channelCfg in _channelsCfg)
                {
                    // this means that channel was already initialized, ensure that channel is not started
                    if (_serverChannels.ContainsKey(channelCfg.ChannelId))
                    {
                        if (_serverChannels[channelCfg.ChannelId].Status)
                        {
                            // todo(umv): server channel is running, handle this properly
                            return new OperationResult()
                            {
                                Success = false,
                                Message = $"Server channel: \"{channelCfg.ChannelId}\" first Stop it and then run Start again"
                            };
                        }

                        if (_serverChannels[channelCfg.ChannelId].Listener == null)
                        {
                            _serverChannels[channelCfg.ChannelId].Listener = new TcpListener(IPAddress.Parse(channelCfg.IpAddress), channelCfg.Port);
                        }
                    }
                    else
                    {
                        _serverChannels[channelCfg.ChannelId] = new TcpChannel()
                        {
                            Status = false,
                            Listener = new TcpListener(IPAddress.Parse(channelCfg.IpAddress), channelCfg.Port)
                        };
                        if (channelCfg.IsSecure)
                        {
                            if (File.Exists(channelCfg.CertificatePath))
                            {
                                _serverChannels[channelCfg.ChannelId].Certificate = X509Certificate.CreateFromSignedFile(channelCfg.CertificatePath);
                                    //.CreateFromCertFile(channelCfg.CertificatePath);
                            }
                            else
                            {
                                _logger.LogWarning($"Certificate file wasn't found on path: \"{channelCfg.CertificatePath}\", starting NON secure channel");
                            }
                        }
                    }
                    // attempt to start
                    _serverChannels[channelCfg.ChannelId].Cancellation = _cancellationSource.Token;
                    _serverChannels[channelCfg.ChannelId].ChannelProcessor = new Thread(ProcessServerChannel);
                        //new Task(ProcessServerChannel, new TcpChannelContext(channelCfg.ChannelId, _serverChannels[channelCfg.ChannelId]),
                                                                                      //_serverChannels[channelCfg.ChannelId].Cancellation);
                    _serverChannels[channelCfg.ChannelId].Listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);
                    _serverChannels[channelCfg.ChannelId].Listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.NoDelay, true);
                    _serverChannels[channelCfg.ChannelId].Listener.Server.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.DontFragment, true);
                    _serverChannels[channelCfg.ChannelId].Listener.Start(ConnectionsQueueSize);
                    _serverChannels[channelCfg.ChannelId].Status = true;

                    _serverChannels[channelCfg.ChannelId].ChannelProcessor.IsBackground = true;
                    _serverChannels[channelCfg.ChannelId].ChannelProcessor.Start(new TcpChannelContext(channelCfg.ChannelId, _serverChannels[channelCfg.ChannelId]));
                }

                _status = true;
                return new OperationResult()
                {
                    Success = true,
                    Message = string.Empty
                };
            }
            catch (Exception e)
            {
                _status = false;
                string reason = $"An error occurred during Tcp server starting: {e.Message}";
                _logger.LogError(reason);
                foreach (ServerChannelConfiguration channelCfg in _channelsCfg)
                {
                    _serverChannels[channelCfg.ChannelId].Listener.Stop();
                    _serverChannels[channelCfg.ChannelId].Status = false;
                }

                return new OperationResult()
                {
                    Success = false,
                    Message = reason
                };
            }
        }

        public async Task<OperationResult> StopAsync()
        {
            try
            {
                if (_status)
                {
                    // 1. Cancel all Tasks
                    _cancellationSource.Cancel();
                    _status = false;
                    // 1. Stop Channel Source ...
                    foreach (KeyValuePair<Guid, TcpChannel> channel in _serverChannels)
                    {
                        channel.Value.Listener.Stop();
                        channel.Value.ChannelProcessor.Join(100);
                    }

                    return new OperationResult()
                    {
                        Success = true,
                        Message = "",
                        Operation = OperationType.StopServer
                    };
                }
                return new OperationResult()
                {
                    Success = false,
                    Message = "Server is not started, nothing to stop",
                    Operation = OperationType.StopServer
                };
            }
            catch (Exception e)
            {
                string msg = $"An error occurred during server Stop: {e.Message}";
                _logger.LogError(msg);
                
                return new OperationResult()
                {
                    Success = false,
                    Message = msg,
                    Operation = OperationType.StopServer
                };
            }
        }

        public async Task<OperationResult> RestartAsync()
        {
            OperationResult result = await StopAsync();
            if (result.Success)
            {
                result = await StartAsync();
                return new OperationResult()
                {
                    Success = result.Success,
                    Message = result.Message,
                    Operation = OperationType.RestartServer
                };
            }
            return new OperationResult()
            {
                Success = false,
                Message = result.Message,
                Operation = OperationType.RestartServer
            };
        }

        public bool IsReady()
        {
            bool result = true;
            foreach (KeyValuePair<Guid,TcpChannel> channel in _serverChannels)
            {
                result &= (channel.Value.ChannelProcessor.IsAlive);
            }
            return _status & result;
        }

        public void DropAllConnectedClients()
        {
            foreach (KeyValuePair<Guid,ClientInfo> client in _clients)
            {
                client.Value.Client.Close();
            }
        }
        
        public void Dispose()
        {
            DropAllConnectedClients();
            _status = false;
            if (!_cancellationSource.IsCancellationRequested)
                _cancellationSource.Cancel();
            foreach (KeyValuePair<Guid, TcpChannel> channel in _serverChannels)
            {
                channel.Value.Listener.Stop();
                if (channel.Value.ChannelProcessor.IsAlive)
                    channel.Value.ChannelProcessor.Join();
            }

        }

        public void AssignHandler(Func<byte[], ClientInfo, Task<byte[]>> packetHandler)
        {
            _packetHandler = packetHandler;
        }

        public void AssignConnectionHandler(Action<ClientInfo> connectionHandler)
        {
            _connectionHandler = connectionHandler;
        }
        
        public void AssignErrorHandler(Action<ErrorType, Exception> errHandler)
        {
            _errHandler = errHandler;
        }

        public async Task<OperationResult> SendDataAsync(Guid clientId, byte[] data)
        {
            try
            {
                if (!_clients.ContainsKey(clientId))
                {
                    return new OperationResult()
                    {
                        Success = false,
                        Operation = OperationType.SendDataToClient,
                        Message = $"Client with id `{clientId}` not found"
                    };
                }

                ClientInfo client = _clients[clientId];
                ServerChannelConfiguration channelCfg = _channelsCfg.First(c => c.ChannelId == client.ChannelId);
                TcpChannel channel = _serverChannels[client.ChannelId];
                Stream ns = GetChannelStream(client, channelCfg.IsSecure, channel);
                await ns.WriteAsync(data, 0, data.Length, _cancellationSource.Token);
                return new OperationResult()
                {
                    Success = true,
                    Operation = OperationType.SendDataToClient,
                };
            }
            catch (Exception e)
            {
                return new OperationResult()
                {
                    Success = false,
                    Operation = OperationType.SendDataToClient,
                    Message = $"An error occured during data send to client: {e.Message}"
                }; 
            }
            
        }

        // state is object identifies channel
        private async void ProcessServerChannel(Object state)
        {
            _logger.LogDebug("Process server channel started");
            TcpChannelContext context = state as TcpChannelContext;
            if (context == null)
            {
                _logger.LogError("Tcp channel was not passed to Channel Process");
                return;
            }

            Task processClientsConnectTask = new Task(async () => 
            {
                _logger.LogDebug("Process client connect task started");
                while (!context.Channel.Cancellation.IsCancellationRequested)
                {
                    try
                    {
                        TcpClient client = await context.Channel.Listener.AcceptTcpClientAsync(context.Channel.Cancellation);
                        // we add new client and remove disconnected too ...
                        Guid clientId = Guid.NewGuid();
                        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                        _clients.Add(clientId, new ClientInfo()
                        {
                            Id = clientId,
                            Client = client,
                            ChannelId = context.ChannelId,
                            LastActivity = DateTimeOffset.UtcNow
                        });
                        if (_connectionHandler != null)
                        {
                            _connectionHandler(_clients[clientId]);
                        }
                    }
                    catch (Exception e)
                    {
                        // todo (UMV): think about err handling
                        if (_errHandler != null)
                        {
                            _errHandler(ErrorType.Connect, e);
                        }

                        _logger.LogError($"An error \"{e.Message}\" occurred during processing client connect");
                    }
                }

                _logger.LogDebug("Process client connect task finished");

            }, context.Channel.Cancellation);

            Task removeDisconnectedClientsTask = new Task(async () =>
            {
                _logger.LogDebug("Remove disconnected clients task started");
                while (!context.Channel.Cancellation.IsCancellationRequested)
                {
                    try
                    {
                         IList<Guid> disconnected = _clients.Where(c => !c.Value.Client.Connected)
                                                            .Select(c => c.Key).ToList();
                        foreach (Guid id in disconnected)
                        {
                            _clients[id].Client.Dispose();
                            _clients.Remove(id);
                        }
                        // raise event
                        await Task.Delay(100, context.Channel.Cancellation);
                    }
                    catch (Exception e)
                    {
                        // todo (UMV): think about err handling
                    }
                }
                _logger.LogDebug("Remove disconnected clients task finished");
            }, context.Channel.Cancellation);

            Task receiveClientsDataPackets = new Task(async () =>
            {
                _logger.LogDebug("Process incoming packets task started");
                while (!context.Channel.Cancellation.IsCancellationRequested)
                {
                    foreach (KeyValuePair<Guid,ClientInfo> client in _clients)
                    {
                        try
                        {
                            bool hasIncomingData = client.Value.Client.Client.Poll(10, SelectMode.SelectRead);
                            if (hasIncomingData)
                            {
                                const int clientBufferSize = 8192; // move this to cfg
                                const int chunkSize = 1024; // move this to cfg
                                Byte[] buffer = new Byte[clientBufferSize];
                                int totalBytesRead = 0;
                                
                                // Get server channel to check whether we should use SSL or not
                                ServerChannelConfiguration channelCfg = _channelsCfg.First(c => c.ChannelId == client.Value.ChannelId);
                                TcpChannel channel = _serverChannels[client.Value.ChannelId];

                                Stream ns = GetChannelStream(client.Value, channelCfg.IsSecure, channel);
                                
                                int offset = 0;
                                int portion = clientBufferSize;

                                while (true)
                                {
                                    // todo(UMV): consider using SslStream 
                                    int bytesRead = await ns.ReadAsync(buffer, offset, clientBufferSize, context.Channel.Cancellation);
                                    totalBytesRead += bytesRead;
                                    if (bytesRead < portion)
                                    {
                                        Array.Resize(ref buffer, bytesRead);
                                        break;
                                    }

                                    offset += bytesRead;
                                    portion = chunkSize;
                                    Array.Resize(ref buffer, totalBytesRead + portion);
                                    client.Value.LastActivity = DateTimeOffset.UtcNow;
                                }

                                // notification about incoming message ...
                                if (_packetHandler != null && totalBytesRead > 0)
                                {
                                    // todo(UMV): add data send
                                    byte[] response = await _packetHandler(buffer, client.Value);
                                    if (response != null && response.Any())
                                    {
                                        Task sendTask = ns.WriteAsync(response, 0, response.Length, context.Channel.Cancellation);
                                        await sendTask;
                                    }
                                }

                            }
                        }
                        catch (Exception e)
                        {
                            // todo (UMV): think about err handling
                            if (_errHandler != null)
                            {
                            }
                        }
                    }
                }

                _logger.LogDebug("Process incoming packets task finished");

            }, context.Channel.Cancellation);

        
            // Run Accept new Clients && Remove disconnected && Poll parallel
            processClientsConnectTask.Start();
            removeDisconnectedClientsTask.Start();
            receiveClientsDataPackets.Start();
                
            await Task.WhenAll(new Task[]
            {
                processClientsConnectTask, 
                removeDisconnectedClientsTask, 
                receiveClientsDataPackets
            });

        }

        private Stream GetChannelStream(ClientInfo client, bool isSecure, TcpChannel channel)
        {
            NetworkStream ns = client.Client.GetStream();
            if (!isSecure)
                return ns;
            SslStream sslStream = new SslStream(ns, false);
            sslStream.AuthenticateAsServer(channel.Certificate, false, true);
            // https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream?view=net-7.0
            return sslStream;
        }

        private const int ConnectionsQueueSize = 100000;

        public IList<ClientInfo> GetClientsStats { get; set; }

        private bool _status;
        private readonly ILogger<MultiChannelTcpServer> _logger;
        private readonly IDictionary<Guid, ClientInfo> _clients;
        private readonly ServerChannelConfiguration[] _channelsCfg;
        private readonly IDictionary<Guid, TcpChannel> _serverChannels;
        private Action<ClientInfo> _connectionHandler;
        private Action<ErrorType, Exception> _errHandler;
        private Func<byte[], ClientInfo, Task<byte[]>> _packetHandler;
        private CancellationTokenSource _cancellationSource;
    }
}