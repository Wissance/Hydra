using System;
using System.Collections.Generic;
using System.Linq;
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
    public class MultiChannelTcpServerTests
    {

        public MultiChannelTcpServerTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        [Theory]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [InlineData(5000)]
        [InlineData(10000)]
        [InlineData(20000)]
        [InlineData(50000)]
        public async Task TestCollectMultipleClientsToOneChannelServer(int clientsNumber)
        {
            Random rand = new Random(DateTimeOffset.Now.Millisecond);
            int serverPort = rand.Next(17000, 25000);
            ServerChannelConfiguration mainInsecureChannel = new ServerChannelConfiguration()
            {
                IpAddress = "127.0.0.1",
                Port = serverPort,
                IsSecure = false
            };
            int clientsCounter = 0;
            int clientsConnectError = 0;
            int serverErrorsCounter = 0;
            
            ITcpServer server = new MultiChannelTcpServer(new[] { mainInsecureChannel }, new NullLoggerFactory(),
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
                clients.Add(new TestTcpClient(true, "127.0.0.1", (UInt16)serverPort));
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
            //await Task.WhenAny(new[]{Task.WhenAll(clientsConnTask), Task.Delay(delayClientsWait)});
            
            //Assert.Equal(0, clientsConnectError);
            Assert.Equal(0, serverErrorsCounter);
            Assert.Equal(clientsNumber, clientsCounter);

            for (int c = 0; c < clientsNumber; c++)
            {
                clients[c].Close();
            }

            Task<OperationResult> stopTask = server.StopAsync();
            stopTask.Wait();
            OperationResult stopResult = stopTask.Result;
            Assert.True(stopResult.Success);
            server.Dispose();
            
            for (int c = 0; c < clientsNumber; c++)
            {
                clients[c].Dispose();
            }
        }

        [Theory]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(5000)]
        [InlineData(10000)]
        public async Task TestInteractWithClients(int clientsNumber)
        {
            Random rand = new Random((int)DateTimeOffset.Now.Ticks);
            int serverPort = rand.Next(17000, 25000);
            ServerChannelConfiguration mainInsecureChannel = new ServerChannelConfiguration()
            {
                IpAddress = "127.0.0.1",
                Port = serverPort,
                IsSecure = false
            };

            ITcpServer server = new MultiChannelTcpServer(new[] { mainInsecureChannel }, new NullLoggerFactory(), 
                null, null);
            int clientsCounter = 0;
            server.AssignConnectionHandler(c => 
            {
                clientsCounter += 1;
            });
            server.AssignHandler( async (d, c) =>
            {
                //_testOutputHelper.WriteLine("Client connected!");
                string msg = System.Text.Encoding.Default.GetString(d);
                //_testOutputHelper.WriteLine($"Client {c.Id} send data: {msg}");
                return d.Reverse().ToArray();
                
            });
            OperationResult startResult  = await server.StartAsync();
            Assert.True(startResult.Success);
            // 2. Create N TcpClients
            IList<TestTcpClient> clients = new List<TestTcpClient>();
            for (int c = 0; c < clientsNumber; c++)
            {
                clients.Add(new TestTcpClient(true, "127.0.0.1", (UInt16)serverPort));
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
        }
        
        private readonly ITestOutputHelper _testOutputHelper;
    }
}