# Hydra

[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![Build Status](https://img.shields.io/badge/build-passing-brightgreen)](https://github.com/Wissance/Hydra)

Hydra is a **high-performance**, **multi-channel** TCP server written on C#. It is designed to handle a massive number of concurrent client connections—tested successfully with up to **50,000 simultaneous clients**. Its key feature is the ability to manage multiple listening sockets (server sockets) within a single server instance, making it a true multi-channel communication hub.

## 🚀 Key Features

*   **Massive Scalability:** Efficiently handles tens of thousands of concurrent TCP client connections.
*   **Multi-Channel Architecture:** Supports creating and managing multiple independent server endpoints (sockets) from a single server process.
*   **High Performance:** Built with `asynchronous I/O` and optimized for minimal resource consumption under heavy load.
*   **Reliability:** Designed for stability, ensuring continuous operation even with a large number of connected clients.
*   **Allows to protect channel with  `TLS` (`X509` certificates) for preventing data from being captured by sniffer
*   

## 🖥️ Usage

Example starting `TCP-server` without `TLS`:

```csharp
// 1. Create Server cofifuration
ServerChannelConfiguration mainInsecureChannel = new ServerChannelConfiguration()
{
    IpAddress = "127.0.0.1",
    Port = 23456,
    IsSecure = false
};

ITcpServer server = new MultiChannelTcpServer(new[] { mainInsecureChannel }, new NullLoggerFactory(), null, null);

// Example Echo server with data reverse
server.AssignHandler( async (d, c) =>
{
    string msg = System.Text.Encoding.Default.GetString(d);
    return d.Reverse().ToArray();
                
});
OperationResult startResult  = await server.StartAsync();
// ...
OperationResult stopResult = await server.StopAsync();
```

## 🧪 Performance & Testing

Hydra has been load-tested to handle up to 50000 concurrent client connections across its channels. Performance metrics depend on hardware, network configuration, and the workload (e.g., message frequency, size).

## :chart_with_upwards_trend: Plans

1. Add Multicahnnel `UDP`
2. Make server that allow to combine `TCP` and `UDP`
3. Measure performance values with `50000` of clients

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request:
1. Fork the repository.
2. Create your feature branch (`git checkout -b feature/AmazingFeature`).
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`).
4. Push to the branch (`git push origin feature/AmazingFeature`).
5. Open a Pull Request.

Please read `CONTRIBUTING.md` for details on our code of conduct and the process for submitting pull requests.

### :smiley_cat: Contributors

`Wissance.Hydra` contributors

<a href="https://github.com/Wissance/Hydra/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=Wissance/Hydra" />
</a>
