using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Wissance.Hydra.Common.Data;
using Wissance.Hydra.Tcp.Configuration;
using Wissance.Hydra.Tcp.Tests.TestUtils;
using Wissance.Hydra.Tcp.Transport;
using Xunit.Abstractions;

namespace Wissance.Hydra.Tcp.Tests.Transport
{
    /*
     * With number of clients > 10000 there could be issue with error on Client.Open:
     *    Error Socket error 10055
     *    An operation on a socket could not be performed because the system lacked sufficient buffer space or because a queue was full
     * This occurs on Windows machine and could be fixed by creating/updating
     * HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\MaxUserPort as DWORD (32-bit) to 65535
     */
    public class MultiChannelTcpServerTests : IDisposable
    {

        public MultiChannelTcpServerTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
            _localAddress = IPAddress.Loopback.ToString();
            string isDocker = Environment.GetEnvironmentVariable(IsContainerizedEnvVar);
            if (!string.IsNullOrEmpty(isDocker) && isDocker.ToLower() == "true")
            {
                _testOutputHelper.WriteLine("Test are running inside a Docker container.");
                _isRunningInContainer = true;
                _localAddress = "0.0.0.0";
            }
            _testOutputHelper.WriteLine($"Local address is: {_localAddress}");
            _connErrorsRatio = 0.05;
        }

        public void Dispose()
        {
        }

        [Theory]
        [InlineData(10000, 100)]
        [InlineData(10001, 500)]
        [InlineData(10002, 1000)]
        [InlineData(10003,5000)]
        [InlineData(10004, 10000)]
        [InlineData(10005, 20000)]
        [InlineData(10006, 50000)]
        public async Task TestCollectMultipleClientsToOneChannelServer(int serverPort, int clientsNumber)
        {
            ServerChannelConfiguration mainInsecureChannel = new ServerChannelConfiguration()
            {
                IpAddress = _localAddress,
                Port = serverPort,
                IsSecure = false,
            };
            _testOutputHelper.WriteLine($"Server port is: {serverPort}");
            
            int clientsCounter = 0;
            int clientsConnectError = 0;
            int serverErrorsCounter = 0;
            
            ITcpServer server = new MultiChannelTcpServer(new[] { mainInsecureChannel }, new LoggerFactory(),
                c =>
                {
                    Interlocked.Increment(ref clientsCounter);
                }, null,
                (errType, exc) =>
                {
                    Interlocked.Increment(ref serverErrorsCounter);
                });
            
            Task<OperationResult> startTask = server.StartAsync();
            startTask.Wait();
            OperationResult startResult = startTask.Result;
            Assert.True(startResult.Success);
            // 2. Create N TcpClients
            IList<TestTcpClient> clients = new List<TestTcpClient>();
            for (int c = 0; c < clientsNumber; c++)
            {
                clients.Add(new TestTcpClient(true, _localAddress, (UInt16)serverPort, 
                    50, 50, 16, false));
            }

            // 3. Open N connections
            IList<Task> clientsConnTask = new List<Task>();
            // when maxTask > 1 some of tests leads to less number of clients
            const int maxTasks = 1;
            int clientsPerTask = (int)Math.Ceiling((double)clientsNumber / maxTasks);
            for (int t = 0; t < maxTasks; t++)
            {
                int tNum = t;
                Task connTask = new Task(async () =>
                {
                    int skip = tNum == 0 ? 0 : tNum * clientsPerTask;
                    IList<TestTcpClient> clientsManagedInTask = clients.Skip(skip).Take(clientsPerTask).ToList();
                    foreach (TestTcpClient client in clientsManagedInTask)
                    {
                        bool result = client.Open();
                        if (!result)
                            Interlocked.Increment(ref clientsConnectError);
                    }
                });
                clientsConnTask.Add(connTask);
                connTask.Start();
            }
            
            await Task.WhenAll(clientsConnTask);
            Assert.Equal(0, serverErrorsCounter);
            int successfullyConnectedClients = (int) ((1.0 - _connErrorsRatio) * clientsNumber);
            Assert.InRange(clientsCounter, successfullyConnectedClients, clientsNumber);

            for (int c = 0; c < clientsNumber; c++)
            {
                clients[c].Close();
            }

            Task<OperationResult> stopTask = server.StopAsync();
            stopTask.Wait();
            OperationResult stopResult = stopTask.Result;
            Assert.True(stopResult.Success);
            
            for (int c = 0; c < clientsNumber; c++)
            {
                clients[c].Dispose();
            }
        }

        [Theory]
        [InlineData(11000, 50)]
        [InlineData(11001, 100)]
        [InlineData(11002, 500)]
        [InlineData(11003,5000)]
        [InlineData(11004, 10000)]
        public async Task TestInteractWithClients(int serverPort, int clientsNumber)
        {
            ServerChannelConfiguration mainInsecureChannel = new ServerChannelConfiguration()
            {
                IpAddress = _localAddress,
                Port = serverPort,
                IsSecure = false
            };
            _testOutputHelper.WriteLine($"Server port is: {serverPort}");

            ITcpServer server = new MultiChannelTcpServer(new[] { mainInsecureChannel }, new LoggerFactory(), 
                null, null);
            int clientsCounter = 0;
            server.AssignConnectionHandler(c => 
            {
                Interlocked.Increment(ref clientsCounter);
            });
            server.AssignHandler(async (d, c) =>
            {
                // _testOutputHelper.WriteLine("Client connected!");
                // string msg = System.Text.Encoding.Default.GetString(d);
                // _testOutputHelper.WriteLine($"Client {c.Id} send data: {msg}");
                return d.Reverse().ToArray();
                
            });
            OperationResult startResult  = await server.StartAsync();
            if (!startResult.Success)
            {
                _testOutputHelper.WriteLine(startResult.Message);
            }
            Assert.True(startResult.Success);
            // 2. Create N TcpClients
            IList<TestTcpClient> clients = new List<TestTcpClient>();
            for (int c = 0; c < clientsNumber; c++)
            {
                clients.Add(new TestTcpClient(true, _localAddress, (UInt16)serverPort));
            }

            // 3. Open N connections
            for (int c = 0; c < clientsNumber; c++)
            {
                clients[c].Open();
            }
            
            // add delay until we get 
            Thread.Sleep(100);
            
            Assert.Equal(clientsNumber, clientsCounter);
            
            for (int c = 0; c < clientsNumber; c++)
            {
                clients[c].Write(new byte[] { 0x31, 0x39, 0x38, 0x34 });
                /*byte[] buffer = new byte[32];
                int bytesRead = 0;
                clients[c].Read(buffer, out bytesRead);
                if (bytesRead > 0)
                {
                    _testOutputHelper.WriteLine("Server respond back");
                }*/
            }
            
            await Task.Delay(1000);

            for (int c = 0; c < clientsNumber; c++)
            {
                byte[] buffer = new byte[32];
                int bytesRead = 0;
                clients[c].Read(buffer, out bytesRead);
                if (bytesRead > 0)
                {
                    string msg = System.Text.Encoding.Default.GetString(buffer.Where(b => b  > 0).ToArray());
                    _testOutputHelper.WriteLine($"Server respond back with: {msg}");
                }
            }

            for (int c = 0; c < clientsNumber; c++)
            {
                clients[c].Close();
            }
            
            
            OperationResult stopResult = await server.StopAsync();
            Assert.True(stopResult.Success);

            for (int c = 0; c < clientsNumber; c++)
            {
                clients[c].Dispose();
            }
            
            server.Dispose();
        }

        [Theory]
        [InlineData(12000, 50)]
        [InlineData(12001, 100)]
        [InlineData(12002, 500)]
        public async Task TestInteractWithClientsWithTlsProtectedChannel(int serverPort, int clientsNumber)
        {
            ServerChannelConfiguration mainSecureChannel = new ServerChannelConfiguration()
            {
                IpAddress = _localAddress,
                Port = serverPort,
                IsSecure = true,
                CertificatePath = Path.GetFullPath(TestCertificatePath)
            };
            _testOutputHelper.WriteLine($"Server port is: {serverPort}");
            
            ITcpServer server = new MultiChannelTcpServer(new[] { mainSecureChannel }, new LoggerFactory(), 
                null, null);
            int clientsCounter = 0;
            server.AssignConnectionHandler(c => 
            {
                Interlocked.Increment(ref clientsCounter);
            });
            server.AssignHandler( async (d, c) =>
            {
                // _testOutputHelper.WriteLine("Client connected!");
                // string msg = System.Text.Encoding.Default.GetString(d);
                // _testOutputHelper.WriteLine($"Client {c.Id} send data: {msg}");
                return d.Reverse().ToArray();
            });
            OperationResult startResult  = await server.StartAsync();
            if (!startResult.Success)
            {
                _testOutputHelper.WriteLine(startResult.Message);
            }

            Assert.True(startResult.Success);
            // 2. Create N TcpClients
            IList<TestTcpClient> clients = new List<TestTcpClient>();
            for (int c = 0; c < clientsNumber; c++)
            {
                clients.Add(new TestTcpClient(true, _localAddress, (UInt16)serverPort, 20, 20, 4, true));
            }

            // 3. Open N connections
            for (int c = 0; c < clientsNumber; c++)
            {
                clients[c].Open();
            }
            
            // add delay until we get 
            await Task.Delay(100);
            
            Assert.Equal(clientsNumber, clientsCounter);
            
            for (int c = 0; c < clientsNumber; c++)
            {
                clients[c].Write(new byte[] { 0x31, 0x39, 0x38, 0x34 });
            }
            
            await Task.Delay(1000);

            for (int c = 0; c < clientsNumber; c++)
            {
                byte[] buffer = new byte[32];
                int bytesRead = 0;
                clients[c].Read(buffer, out bytesRead);
                /*if (bytesRead > 0)
                {
                    string msg = System.Text.Encoding.Default.GetString(buffer.Where(b => b  > 0).ToArray());
                    _testOutputHelper.WriteLine($"Server respond back with: {msg}");
                }*/
            }

            for (int c = 0; c < clientsNumber; c++)
            {
                clients[c].Close();
            }
            
            
            OperationResult stopResult = await server.StopAsync();
            Assert.True(stopResult.Success);

            for (int c = 0; c < clientsNumber; c++)
            {
                clients[c].Dispose();
            }
            
            server.Dispose();
        }

        [Theory]
        [InlineData(13000, 50, 13001, 100)]
        public async Task TestMultiChannelTcpServer(int mainServerPort, int mainChannelClientsNumber, 
            int additionalServerPort, int additionalChannelClientsNumber)
        {
            Random rand = new Random((int)DateTimeOffset.Now.Ticks);
            ServerChannelConfiguration mainSecureChannel = new ServerChannelConfiguration()
            {
                IpAddress = _localAddress,
                Port = mainServerPort,
                IsSecure = true,
                CertificatePath = Path.GetFullPath(TestCertificatePath)
            };
            ServerChannelConfiguration additionalNonSecureChannel = new ServerChannelConfiguration()
            {
                IpAddress = _localAddress,
                Port = additionalServerPort,
                IsSecure = false
            };
            
            _testOutputHelper.WriteLine($"Server port are: ({mainServerPort} , {additionalServerPort})");
            
            ITcpServer server = new MultiChannelTcpServer(new[] { mainSecureChannel, additionalNonSecureChannel }, 
                new NullLoggerFactory(), 
                null, null);
            int clientsCounter = 0;
            server.AssignConnectionHandler(c => 
            {
                Interlocked.Increment(ref clientsCounter);
            });
            server.AssignHandler( async (d, c) =>
            {
                // _testOutputHelper.WriteLine("Client connected!");
                // string msg = System.Text.Encoding.Default.GetString(d);
                // _testOutputHelper.WriteLine($"Client {c.Id} send data: {msg}");
                return d.Reverse().ToArray();
            });
            OperationResult startResult  = await server.StartAsync();
            if (!startResult.Success)
            {
                _testOutputHelper.WriteLine(startResult.Message);
            }
            Assert.True(startResult.Success);
            
            IList<TestTcpClient> clients = new List<TestTcpClient>();
            for (int c = 0; c < mainChannelClientsNumber; c++)
            {
                clients.Add(new TestTcpClient(true, _localAddress, (UInt16)mainServerPort, 20, 20, 4, true));
            }
            
            for (int c = 0; c < additionalChannelClientsNumber; c++)
            {
                clients.Add(new TestTcpClient(true, _localAddress, (UInt16)additionalServerPort, 20, 20, 4, false));
            }

            // 3. Open N connections
            for (int c = 0; c < clients.Count; c++)
            {
                clients[c].Open();
            }
            
            // add delay until we get 
            await Task.Delay(100);
            
            Assert.Equal(clients.Count, clientsCounter);
            
            for (int c = 0; c < clients.Count; c++)
            {
                clients[c].Write(new byte[] { 0x31, 0x39, 0x38, 0x34 });
            }
            
            await Task.Delay(1000);

            for (int c = 0; c < clients.Count; c++)
            {
                byte[] buffer = new byte[32];
                int bytesRead = 0;
                clients[c].Read(buffer, out bytesRead);
                /*if (bytesRead > 0)
                {
                    string msg = System.Text.Encoding.Default.GetString(buffer.Where(b => b  > 0).ToArray());
                    _testOutputHelper.WriteLine($"Server respond back with: {msg}");
                }*/
            }

            for (int c = 0; c < clients.Count; c++)
            {
                clients[c].Close();
            }
            
            OperationResult stopResult = await server.StopAsync();
            Assert.True(stopResult.Success);
            
            server.Dispose();
        }

        private const string IsContainerizedEnvVar = "IS_CONTAINERIZED";
        private const string TestCertificatePath = "../../../testCerts/certificate.pfx";
        
        private readonly string _localAddress;
        private readonly ITestOutputHelper _testOutputHelper;
        // In test.Dockerfile there must be an ENV IS_CONTAINERIZED (ENV IS_CONTAINERIZED=true )
        private bool _isRunningInContainer;
        private double _connErrorsRatio;
    }
}