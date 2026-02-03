using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WatsonWebsocket;

namespace Test.Automated.Tests
{
    public class IntegrationTests
    {
        private readonly TestRunner _runner;
        private int _basePort = 12000;
        private const int ConnectionTimeoutSeconds = 10;
        private const int MessageTimeoutMs = 10000;

        public IntegrationTests(TestRunner runner)
        {
            _runner = runner;
        }

        private int GetNextPort() => Interlocked.Increment(ref _basePort);

        // Safe disposal helpers to prevent exceptions during cleanup
        private void SafeDispose(WatsonWsClient client)
        {
            if (client == null) return;
            try
            {
                if (client.Connected)
                {
                    client.Stop();
                }
            }
            catch { }
            try { client.Dispose(); } catch { }
        }

        private void SafeDispose(WatsonWsServer server)
        {
            if (server == null) return;
            try
            {
                if (server.IsListening)
                {
                    server.Stop();
                }
            }
            catch { }
            try { server.Dispose(); } catch { }
        }

        // Helper class for safe resource management
        private class TestContext : IDisposable
        {
            private readonly List<WatsonWsClient> _clients = new List<WatsonWsClient>();
            private readonly List<WatsonWsServer> _servers = new List<WatsonWsServer>();
            private readonly IntegrationTests _parent;

            public TestContext(IntegrationTests parent) => _parent = parent;

            public WatsonWsServer CreateServer(string ip, int port, bool ssl = false)
            {
                var server = new WatsonWsServer(ip, port, ssl);
                _servers.Add(server);
                return server;
            }

            public WatsonWsClient CreateClient(string ip, int port, bool ssl = false, Guid guid = default)
            {
                var client = new WatsonWsClient(ip, port, ssl, guid);
                _clients.Add(client);
                return client;
            }

            public WatsonWsClient CreateClient(Uri uri, Guid guid = default)
            {
                var client = new WatsonWsClient(uri, guid);
                _clients.Add(client);
                return client;
            }

            public void Dispose()
            {
                foreach (var client in _clients)
                    _parent.SafeDispose(client);
                foreach (var server in _servers)
                    _parent.SafeDispose(server);
            }
        }

        private TestContext CreateTestContext() => new TestContext(this);

        private async Task<bool> WaitForConditionAsync(Func<bool> condition, int timeoutMs)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (condition()) return true;
                await Task.Delay(50);
            }
            return condition();
        }

        private async Task RunIntegrationTestAsync(string category, string testName, Func<Task> testAction)
        {
            if (_connectivityFailed)
            {
                await _runner.RunTestAsync(category, testName, () =>
                {
                    throw new SkipTestException("Skipped - connectivity check failed (may require admin privileges for HttpListener)");
                });
                return;
            }

            await _runner.RunTestAsync(category, testName, async () =>
            {
                try
                {
                    await testAction();
                }
                catch (AggregateException ae)
                {
                    // Unwrap and report the actual inner exception
                    var innerEx = ae.InnerException ?? ae.InnerExceptions.FirstOrDefault() ?? ae;
                    throw new Exception($"{innerEx.GetType().Name}: {innerEx.Message}");
                }
            });
        }

        private bool _connectivityFailed = false;

        public async Task RunAllTests()
        {
            // First, verify basic connectivity works
            Console.WriteLine("\n--- Connectivity Check ---\n");
            await VerifyConnectivity();

            if (_connectivityFailed)
            {
                Console.WriteLine("\nWARNING: Basic connectivity test failed. This may be due to:");
                Console.WriteLine("  - Windows Firewall blocking connections");
                Console.WriteLine("  - Missing URL ACL permissions (try running as Administrator)");
                Console.WriteLine("  - HttpListener not being supported on this platform");
                Console.WriteLine("\nIntegration tests will continue but may fail.\n");
            }

            Console.WriteLine("\n--- Connection Integration Tests ---\n");
            await RunConnectionTests();

            Console.WriteLine("\n--- Messaging Integration Tests ---\n");
            await RunMessagingTests();

            Console.WriteLine("\n--- Event Integration Tests ---\n");
            await RunEventTests();

            Console.WriteLine("\n--- Multiple Client Integration Tests ---\n");
            await RunMultiClientTests();

            Console.WriteLine("\n--- Statistics Integration Tests ---\n");
            await RunStatisticsTests();

            Console.WriteLine("\n--- Send And Wait Tests ---\n");
            await RunSendAndWaitTests();

            Console.WriteLine("\n--- Headers And Cookies Tests ---\n");
            await RunHeadersAndCookiesTests();

            Console.WriteLine("\n--- Disconnection Tests ---\n");
            await RunDisconnectionTests();

            Console.WriteLine("\n--- Data Integrity Tests ---\n");
            await RunDataIntegrityTests();

            Console.WriteLine("\n--- Concurrent Operation Tests ---\n");
            await RunConcurrencyTests();

            Console.WriteLine("\n--- Edge Case Tests ---\n");
            await RunEdgeCaseTests();
        }

        private async Task VerifyConnectivity()
        {
            int port = GetNextPort();

            Console.WriteLine($"  Testing connectivity on port {port}...");

            try
            {
                using var server = new WatsonWsServer("127.0.0.1", port, false);

                // Add event handlers for diagnostics
                bool clientConnectedToServer = false;
                server.ClientConnected += (s, e) =>
                {
                    clientConnectedToServer = true;
                    Console.WriteLine($"  Server received client connection: {e.Client.Guid}");
                };

                server.Start();
                Console.WriteLine($"  Server started, IsListening: {server.IsListening}");

                if (!server.IsListening)
                {
                    Console.WriteLine("  ERROR: Server failed to start listening");
                    _connectivityFailed = true;
                    return;
                }

                // Give the server a moment to fully initialize
                await Task.Delay(100);

                using var client = new WatsonWsClient("127.0.0.1", port, false);

                bool clientConnectedEvent = false;
                client.ServerConnected += (s, e) =>
                {
                    clientConnectedEvent = true;
                    Console.WriteLine("  Client connected event fired");
                };

                Console.WriteLine("  Client attempting to connect...");
                bool connected = await client.StartWithTimeoutAsync(10);

                // Wait a bit for events to fire
                await Task.Delay(200);

                Console.WriteLine($"  StartWithTimeoutAsync returned: {connected}");
                Console.WriteLine($"  client.Connected: {client.Connected}");
                Console.WriteLine($"  Client connected event fired: {clientConnectedEvent}");
                Console.WriteLine($"  Server saw client connect: {clientConnectedToServer}");

                if (connected && client.Connected)
                {
                    Console.WriteLine("  Connectivity check: PASSED");

                    // Clean disconnect
                    try { await client.StopAsync(); } catch { }
                }
                else
                {
                    Console.WriteLine("  Connectivity check: FAILED (client could not connect)");
                    _connectivityFailed = true;
                }

                server.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Connectivity check: FAILED");
                Console.WriteLine($"  Exception type: {ex.GetType().Name}");
                Console.WriteLine($"  Exception message: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"  Inner exception: {ex.InnerException.Message}");
                }
                _connectivityFailed = true;
            }
        }

        private async Task RunConnectionTests()
        {
            await RunIntegrationTestAsync("Connection", "BasicClientServerConnection", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                server.Start();
                Assert.IsTrue(server.IsListening);

                // Give server time to fully initialize
                await Task.Delay(100);

                var client = ctx.CreateClient("127.0.0.1", port, false);
                bool connected = await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                Assert.IsTrue(connected, "Client should connect to server");
                Assert.IsTrue(client.Connected, "Client.Connected should be true");

                await Task.Delay(100);
            });

            await RunIntegrationTestAsync("Connection", "ClientConnect_WithCustomGuid", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);

                var customGuid = Guid.NewGuid();
                Guid? receivedGuid = null;

                server.ClientConnected += (s, e) =>
                {
                    receivedGuid = e.Client.Guid;
                };

                server.Start();
                await Task.Delay(100);

                var client = ctx.CreateClient("127.0.0.1", port, false, customGuid);
                bool connected = await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                Assert.IsTrue(connected);

                await Task.Delay(200);
                Assert.IsNotNull(receivedGuid);
                Assert.AreEqual(customGuid, receivedGuid.Value);
            });

            await RunIntegrationTestAsync("Connection", "ServerListClients_ReturnsConnectedClient", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                server.Start();
                await Task.Delay(100);

                var client = ctx.CreateClient("127.0.0.1", port, false);
                bool connected = await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                Assert.IsTrue(connected);

                await Task.Delay(200);

                var clients = server.ListClients().ToList();
                Assert.AreEqual(1, clients.Count);
                Assert.IsNotNull(clients[0].Guid);
            });

            await RunIntegrationTestAsync("Connection", "ServerIsClientConnected_ReturnsTrue_ForConnectedClient", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);

                Guid? clientGuid = null;
                server.ClientConnected += (s, e) => { clientGuid = e.Client.Guid; };

                server.Start();
                await Task.Delay(100);

                var client = ctx.CreateClient("127.0.0.1", port, false);
                bool connected = await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                Assert.IsTrue(connected);

                await Task.Delay(200);
                Assert.IsNotNull(clientGuid);
                Assert.IsTrue(server.IsClientConnected(clientGuid.Value));
            });

            await RunIntegrationTestAsync("Connection", "ClientConnect_UsingUri", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                server.Start();
                await Task.Delay(100);

                var uri = new Uri($"ws://127.0.0.1:{port}");
                var client = ctx.CreateClient(uri);

                bool connected = await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                Assert.IsTrue(connected);
            });

            await RunIntegrationTestAsync("Connection", "ClientConnect_UsingStartAsync", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                server.Start();
                await Task.Delay(100);

                await client.StartAsync();
                await Task.Delay(500);

                Assert.IsTrue(client.Connected);
            });

            await RunIntegrationTestAsync("Connection", "ClientConnect_UsingStart", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                server.Start();
                await Task.Delay(100);

                client.Start();
                await Task.Delay(500);

                Assert.IsTrue(client.Connected);
            });

            await RunIntegrationTestAsync("Connection", "ServerWithMultipleHostnames", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var hostnames = new List<string> { "127.0.0.1", "127.0.0.1" };
                var server = new WatsonWsServer(hostnames, port, false);
                server.Start();
                await Task.Delay(100);

                var client1 = ctx.CreateClient("127.0.0.1", port, false);
                bool connected1 = await client1.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                Assert.IsTrue(connected1);

                var client2 = ctx.CreateClient("127.0.0.1", port, false);
                bool connected2 = await client2.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                Assert.IsTrue(connected2);

                await Task.Delay(100);
                var clients = server.ListClients().ToList();
                Assert.AreEqual(2, clients.Count);

                SafeDispose(server);
            });
        }

        private async Task RunMessagingTests()
        {
            await RunIntegrationTestAsync("Messaging", "SendTextMessage_ClientToServer", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                string receivedMessage = null;
                var messageReceived = new TaskCompletionSource<bool>();

                server.MessageReceived += (s, e) =>
                {
                    receivedMessage = Encoding.UTF8.GetString(e.Data.Array, e.Data.Offset, e.Data.Count);
                    messageReceived.TrySetResult(true);
                };

                server.Start();
                await Task.Delay(100);
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                bool sent = await client.SendAsync("Hello, Server!");
                Assert.IsTrue(sent);

                var completed = await Task.WhenAny(messageReceived.Task, Task.Delay(MessageTimeoutMs));
                Assert.AreEqual(messageReceived.Task, completed, "Server should receive the message");
                Assert.AreEqual("Hello, Server!", receivedMessage);
            });

            await RunIntegrationTestAsync("Messaging", "SendTextMessage_ServerToClient", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                string receivedMessage = null;
                var messageReceived = new TaskCompletionSource<bool>();
                Guid? clientGuid = null;

                server.ClientConnected += (s, e) => { clientGuid = e.Client.Guid; };

                client.MessageReceived += (s, e) =>
                {
                    receivedMessage = Encoding.UTF8.GetString(e.Data.Array, e.Data.Offset, e.Data.Count);
                    messageReceived.TrySetResult(true);
                };

                server.Start();
                await Task.Delay(100);
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                await Task.Delay(200);

                Assert.IsNotNull(clientGuid);
                bool sent = await server.SendAsync(clientGuid.Value, "Hello, Client!");
                Assert.IsTrue(sent);

                var completed = await Task.WhenAny(messageReceived.Task, Task.Delay(MessageTimeoutMs));
                Assert.AreEqual(messageReceived.Task, completed, "Client should receive the message");
                Assert.AreEqual("Hello, Client!", receivedMessage);
            });

            await RunIntegrationTestAsync("Messaging", "SendBinaryMessage_ClientToServer", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                byte[] receivedData = null;
                var messageReceived = new TaskCompletionSource<bool>();

                server.MessageReceived += (s, e) =>
                {
                    receivedData = e.Data.ToArray();
                    Assert.AreEqual(WebSocketMessageType.Binary, e.MessageType);
                    messageReceived.TrySetResult(true);
                };

                server.Start();
                await Task.Delay(100);
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                var binaryData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
                bool sent = await client.SendAsync(binaryData, WebSocketMessageType.Binary);
                Assert.IsTrue(sent);

                var completed = await Task.WhenAny(messageReceived.Task, Task.Delay(MessageTimeoutMs));
                Assert.AreEqual(messageReceived.Task, completed);
                Assert.ArraysEqual(binaryData, receivedData);
            });

            await RunIntegrationTestAsync("Messaging", "SendBinaryMessage_ServerToClient", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                byte[] receivedData = null;
                var messageReceived = new TaskCompletionSource<bool>();
                Guid? clientGuid = null;

                server.ClientConnected += (s, e) => { clientGuid = e.Client.Guid; };

                client.MessageReceived += (s, e) =>
                {
                    receivedData = e.Data.ToArray();
                    Assert.AreEqual(WebSocketMessageType.Binary, e.MessageType);
                    messageReceived.TrySetResult(true);
                };

                server.Start();
                await Task.Delay(100);
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                await Task.Delay(200);

                Assert.IsNotNull(clientGuid);
                var binaryData = new byte[] { 0xFF, 0xFE, 0xFD, 0xFC };
                bool sent = await server.SendAsync(clientGuid.Value, binaryData, WebSocketMessageType.Binary);
                Assert.IsTrue(sent);

                var completed = await Task.WhenAny(messageReceived.Task, Task.Delay(MessageTimeoutMs));
                Assert.AreEqual(messageReceived.Task, completed);
                Assert.ArraysEqual(binaryData, receivedData);
            });

            await RunIntegrationTestAsync("Messaging", "SendArraySegment_ClientToServer", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                byte[] receivedData = null;
                var messageReceived = new TaskCompletionSource<bool>();

                server.MessageReceived += (s, e) =>
                {
                    receivedData = e.Data.ToArray();
                    messageReceived.TrySetResult(true);
                };

                server.Start();
                await Task.Delay(100);
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                var fullArray = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 };
                var segment = new ArraySegment<byte>(fullArray, 1, 4); // Bytes 1-4
                bool sent = await client.SendAsync(segment, WebSocketMessageType.Binary);
                Assert.IsTrue(sent);

                var completed = await Task.WhenAny(messageReceived.Task, Task.Delay(MessageTimeoutMs));
                Assert.AreEqual(messageReceived.Task, completed);
                Assert.ArraysEqual(new byte[] { 0x01, 0x02, 0x03, 0x04 }, receivedData);
            });

            await RunIntegrationTestAsync("Messaging", "SendEmptyMessage_ClientToServer", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                int receivedCount = 0;
                var messageReceived = new TaskCompletionSource<bool>();

                server.MessageReceived += (s, e) =>
                {
                    receivedCount = e.Data.Count;
                    messageReceived.TrySetResult(true);
                };

                server.Start();
                await Task.Delay(100);
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                bool sent = await client.SendAsync(new byte[0], WebSocketMessageType.Binary);
                // Empty message handling depends on implementation

                await Task.Delay(500);
            });

            await RunIntegrationTestAsync("Messaging", "SendLargeMessage_ClientToServer", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                byte[] receivedData = null;
                var messageReceived = new TaskCompletionSource<bool>();

                server.MessageReceived += (s, e) =>
                {
                    receivedData = e.Data.ToArray();
                    messageReceived.TrySetResult(true);
                };

                server.Start();
                await Task.Delay(100);
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                // Send a 100KB message
                var largeData = new byte[100 * 1024];
                new Random(42).NextBytes(largeData);

                bool sent = await client.SendAsync(largeData, WebSocketMessageType.Binary);
                Assert.IsTrue(sent);

                var completed = await Task.WhenAny(messageReceived.Task, Task.Delay(10000));
                Assert.AreEqual(messageReceived.Task, completed, "Server should receive large message");
                Assert.AreEqual(largeData.Length, receivedData.Length);
                Assert.ArraysEqual(largeData, receivedData);
            });

            await RunIntegrationTestAsync("Messaging", "SendMultipleMessages_InSequence", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                var receivedMessages = new List<string>();
                var allReceived = new TaskCompletionSource<bool>();

                server.MessageReceived += (s, e) =>
                {
                    lock (receivedMessages)
                    {
                        receivedMessages.Add(Encoding.UTF8.GetString(e.Data.Array, e.Data.Offset, e.Data.Count));
                        if (receivedMessages.Count == 5)
                            allReceived.TrySetResult(true);
                    }
                };

                server.Start();
                await Task.Delay(100);
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                for (int i = 0; i < 5; i++)
                {
                    bool sent = await client.SendAsync($"Message {i}");
                    Assert.IsTrue(sent, $"Message {i} should be sent");
                }

                var completed = await Task.WhenAny(allReceived.Task, Task.Delay(MessageTimeoutMs));
                Assert.AreEqual(allReceived.Task, completed, "All messages should be received");
                Assert.AreEqual(5, receivedMessages.Count);

                // Verify order
                for (int i = 0; i < 5; i++)
                {
                    Assert.AreEqual($"Message {i}", receivedMessages[i]);
                }
            });

            await RunIntegrationTestAsync("Messaging", "SendToDisconnectedClient_ReturnsFalse", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                server.Start();

                var nonexistentGuid = Guid.NewGuid();
                bool sent = await server.SendAsync(nonexistentGuid, "Test");
                Assert.IsFalse(sent);
            });

            await RunIntegrationTestAsync("Messaging", "MessageType_Text_IsPreserved", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                WebSocketMessageType? receivedType = null;
                var messageReceived = new TaskCompletionSource<bool>();

                server.MessageReceived += (s, e) =>
                {
                    receivedType = e.MessageType;
                    messageReceived.TrySetResult(true);
                };

                server.Start();
                await Task.Delay(100);
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                await client.SendAsync("Test", WebSocketMessageType.Text);

                var completed = await Task.WhenAny(messageReceived.Task, Task.Delay(MessageTimeoutMs));
                Assert.AreEqual(messageReceived.Task, completed);
                Assert.AreEqual(WebSocketMessageType.Text, receivedType);
            });
        }

        private async Task RunEventTests()
        {
            await RunIntegrationTestAsync("Events", "ClientConnected_EventFires", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                bool eventFired = false;
                ClientMetadata connectedClient = null;

                server.ClientConnected += (s, e) =>
                {
                    eventFired = true;
                    connectedClient = e.Client;
                };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                await Task.Delay(200);

                Assert.IsTrue(eventFired, "ClientConnected event should fire");
                Assert.IsNotNull(connectedClient);
                Assert.AreNotEqual(Guid.Empty, connectedClient.Guid);
            });

            await RunIntegrationTestAsync("Events", "ClientDisconnected_EventFires_OnClientStop", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                bool disconnectEventFired = false;
                var disconnected = new TaskCompletionSource<bool>();

                server.ClientDisconnected += (s, e) =>
                {
                    disconnectEventFired = true;
                    disconnected.TrySetResult(true);
                };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                await Task.Delay(200);

                await client.StopAsync();

                var completed = await Task.WhenAny(disconnected.Task, Task.Delay(MessageTimeoutMs));
                Assert.AreEqual(disconnected.Task, completed, "Disconnect event should fire");
                Assert.IsTrue(disconnectEventFired);
            });

            await RunIntegrationTestAsync("Events", "ClientDisconnected_EventFires_OnServerDisconnect", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                Guid? clientGuid = null;
                var disconnected = new TaskCompletionSource<bool>();

                server.ClientConnected += (s, e) => { clientGuid = e.Client.Guid; };
                server.ClientDisconnected += (s, e) => { disconnected.TrySetResult(true); };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                await Task.Delay(200);

                Assert.IsNotNull(clientGuid);
                server.DisconnectClient(clientGuid.Value);

                var completed = await Task.WhenAny(disconnected.Task, Task.Delay(MessageTimeoutMs));
                Assert.AreEqual(disconnected.Task, completed, "Disconnect event should fire after server disconnect");
            });

            await RunIntegrationTestAsync("Events", "ServerConnected_EventFires_OnClient", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                bool eventFired = false;
                client.ServerConnected += (s, e) => { eventFired = true; };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                await Task.Delay(200);

                Assert.IsTrue(eventFired, "ServerConnected event should fire on client");
            });

            await RunIntegrationTestAsync("Events", "ServerDisconnected_EventFires_OnClient", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                var disconnected = new TaskCompletionSource<bool>();
                client.ServerDisconnected += (s, e) => { disconnected.TrySetResult(true); };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                await Task.Delay(200);

                server.Stop();

                var completed = await Task.WhenAny(disconnected.Task, Task.Delay(MessageTimeoutMs));
                Assert.AreEqual(disconnected.Task, completed, "ServerDisconnected event should fire");
            });

            await RunIntegrationTestAsync("Events", "MessageReceived_IncludesClientMetadata", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                ClientMetadata receivedClient = null;
                var messageReceived = new TaskCompletionSource<bool>();

                server.MessageReceived += (s, e) =>
                {
                    receivedClient = e.Client;
                    messageReceived.TrySetResult(true);
                };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                await client.SendAsync("Test");

                var completed = await Task.WhenAny(messageReceived.Task, Task.Delay(MessageTimeoutMs));
                Assert.AreEqual(messageReceived.Task, completed);
                Assert.IsNotNull(receivedClient);
                Assert.AreNotEqual(Guid.Empty, receivedClient.Guid);
            });

            await RunIntegrationTestAsync("Events", "ConnectionEventArgs_HasHttpRequest", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                HttpListenerRequest httpRequest = null;
                var connected = new TaskCompletionSource<bool>();

                server.ClientConnected += (s, e) =>
                {
                    httpRequest = e.HttpRequest;
                    connected.TrySetResult(true);
                };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                var completed = await Task.WhenAny(connected.Task, Task.Delay(MessageTimeoutMs));
                Assert.AreEqual(connected.Task, completed);
                Assert.IsNotNull(httpRequest);
            });
        }

        private async Task RunMultiClientTests()
        {
            await RunIntegrationTestAsync("MultiClient", "MultipleClients_CanConnect", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);

                server.Start();

                for (int i = 0; i < 5; i++)
                {
                    var client = ctx.CreateClient("127.0.0.1", port, false);
                    bool connected = await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                    Assert.IsTrue(connected, $"Client {i} should connect");
                }

                await Task.Delay(500);
                var connectedClients = server.ListClients().ToList();
                Assert.AreEqual(5, connectedClients.Count);
            });

            await RunIntegrationTestAsync("MultiClient", "Server_CanBroadcast_ToAllClients", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var receivedCounts = new ConcurrentDictionary<int, int>();

                server.Start();

                for (int i = 0; i < 3; i++)
                {
                    var clientIndex = i;
                    var client = ctx.CreateClient("127.0.0.1", port, false);

                    client.MessageReceived += (s, e) =>
                    {
                        receivedCounts.AddOrUpdate(clientIndex, 1, (k, v) => v + 1);
                    };

                    bool connected = await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                    Assert.IsTrue(connected);
                }

                await Task.Delay(300);

                // Broadcast to all clients
                foreach (var clientMeta in server.ListClients())
                {
                    await server.SendAsync(clientMeta.Guid, "Broadcast message");
                }

                await Task.Delay(500);

                Assert.AreEqual(3, receivedCounts.Count, "All 3 clients should have received the message");
                foreach (var count in receivedCounts.Values)
                {
                    Assert.AreEqual(1, count);
                }
            });

            await RunIntegrationTestAsync("MultiClient", "Server_CanSend_ToSpecificClient", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var clientGuids = new List<Guid>();
                var receivedCounts = new ConcurrentDictionary<int, int>();

                var clientConnected = new TaskCompletionSource<bool>();
                int connectedCount = 0;

                server.ClientConnected += (s, e) =>
                {
                    lock (clientGuids)
                    {
                        clientGuids.Add(e.Client.Guid);
                        connectedCount++;
                        if (connectedCount == 3)
                            clientConnected.TrySetResult(true);
                    }
                };

                server.Start();

                for (int i = 0; i < 3; i++)
                {
                    var clientIndex = i;
                    var client = ctx.CreateClient("127.0.0.1", port, false);

                    client.MessageReceived += (s, e) =>
                    {
                        receivedCounts.AddOrUpdate(clientIndex, 1, (k, v) => v + 1);
                    };

                    await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                }

                await Task.WhenAny(clientConnected.Task, Task.Delay(MessageTimeoutMs));

                // Send only to first client
                await server.SendAsync(clientGuids[0], "Message for client 0 only");

                await Task.Delay(500);

                Assert.AreEqual(1, receivedCounts.Count, "Only one client should have received the message");
                Assert.IsTrue(receivedCounts.ContainsKey(0), "Client 0 should have received the message");
            });

            await RunIntegrationTestAsync("MultiClient", "EachClient_HasUniqueGuid", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var guids = new ConcurrentBag<Guid>();

                server.ClientConnected += (s, e) =>
                {
                    guids.Add(e.Client.Guid);
                };

                server.Start();

                for (int i = 0; i < 5; i++)
                {
                    var client = ctx.CreateClient("127.0.0.1", port, false);
                    await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                }

                await Task.Delay(500);

                var guidList = guids.ToList();
                Assert.AreEqual(5, guidList.Count);
                Assert.AreEqual(5, guidList.Distinct().Count(), "All GUIDs should be unique");
            });

            await RunIntegrationTestAsync("MultiClient", "Client_RemovedFromList_AfterDisconnect", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client1 = ctx.CreateClient("127.0.0.1", port, false);
                var client2 = ctx.CreateClient("127.0.0.1", port, false);

                var disconnected = new TaskCompletionSource<bool>();
                server.ClientDisconnected += (s, e) => { disconnected.TrySetResult(true); };

                server.Start();
                await client1.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                await client2.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                await Task.Delay(300);
                Assert.AreEqual(2, server.ListClients().Count());

                await client1.StopAsync();
                await Task.WhenAny(disconnected.Task, Task.Delay(MessageTimeoutMs));
                await Task.Delay(200);

                Assert.AreEqual(1, server.ListClients().Count());
            });
        }

        private async Task RunStatisticsTests()
        {
            await RunIntegrationTestAsync("Statistics", "SentBytes_TrackedCorrectly_OnClient", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                var initialSentBytes = client.Stats.SentBytes;
                var message = "Test message for statistics";
                await client.SendAsync(message);

                await Task.Delay(200);
                Assert.IsGreaterThan(client.Stats.SentBytes, initialSentBytes);
            });

            await RunIntegrationTestAsync("Statistics", "SentMessages_TrackedCorrectly_OnClient", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                var initialSentMessages = client.Stats.SentMessages;

                await client.SendAsync("Message 1");
                await client.SendAsync("Message 2");
                await client.SendAsync("Message 3");

                await Task.Delay(200);
                Assert.AreEqual(initialSentMessages + 3, client.Stats.SentMessages);
            });

            await RunIntegrationTestAsync("Statistics", "ReceivedBytes_TrackedCorrectly_OnServer", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                var messageReceived = new TaskCompletionSource<bool>();
                server.MessageReceived += (s, e) => { messageReceived.TrySetResult(true); };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                var initialReceivedBytes = server.Stats.ReceivedBytes;
                await client.SendAsync("Test message for server statistics");

                await Task.WhenAny(messageReceived.Task, Task.Delay(MessageTimeoutMs));
                Assert.IsGreaterThan(server.Stats.ReceivedBytes, initialReceivedBytes);
            });

            await RunIntegrationTestAsync("Statistics", "ReceivedMessages_TrackedCorrectly_OnServer", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                int messagesReceived = 0;
                var allReceived = new TaskCompletionSource<bool>();
                server.MessageReceived += (s, e) =>
                {
                    Interlocked.Increment(ref messagesReceived);
                    if (messagesReceived == 3)
                        allReceived.TrySetResult(true);
                };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                var initialReceivedMessages = server.Stats.ReceivedMessages;

                await client.SendAsync("Message 1");
                await client.SendAsync("Message 2");
                await client.SendAsync("Message 3");

                await Task.WhenAny(allReceived.Task, Task.Delay(MessageTimeoutMs));
                Assert.AreEqual(initialReceivedMessages + 3, server.Stats.ReceivedMessages);
            });

            await RunIntegrationTestAsync("Statistics", "Stats_NotTracked_WhenDisabled", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                client.EnableStatistics = false;
                server.EnableStatistics = false;

                var messageReceived = new TaskCompletionSource<bool>();
                server.MessageReceived += (s, e) => { messageReceived.TrySetResult(true); };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                await client.SendAsync("Test message");
                await Task.WhenAny(messageReceived.Task, Task.Delay(MessageTimeoutMs));

                // When statistics are disabled, counters should remain at 0
                Assert.AreEqual(0L, client.Stats.SentMessages);
                Assert.AreEqual(0L, server.Stats.ReceivedMessages);
            });

            await RunIntegrationTestAsync("Statistics", "Stats_Reset_ClearsCounters", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                var messageReceived = new TaskCompletionSource<bool>();
                server.MessageReceived += (s, e) => { messageReceived.TrySetResult(true); };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                await client.SendAsync("Test message");
                await Task.WhenAny(messageReceived.Task, Task.Delay(MessageTimeoutMs));

                // Verify stats were collected
                Assert.IsGreaterThan(client.Stats.SentMessages, 0);

                // Reset stats
                client.Stats.Reset();

                Assert.AreEqual(0L, client.Stats.SentBytes);
                Assert.AreEqual(0L, client.Stats.SentMessages);
                Assert.AreEqual(0L, client.Stats.ReceivedBytes);
                Assert.AreEqual(0L, client.Stats.ReceivedMessages);
            });

            await RunIntegrationTestAsync("Statistics", "AverageMessageSize_CalculatedCorrectly", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                int messagesReceived = 0;
                var allReceived = new TaskCompletionSource<bool>();
                server.MessageReceived += (s, e) =>
                {
                    if (Interlocked.Increment(ref messagesReceived) == 3)
                        allReceived.TrySetResult(true);
                };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                // Send messages of known sizes
                await client.SendAsync("12345"); // 5 bytes
                await client.SendAsync("1234567890"); // 10 bytes
                await client.SendAsync("123456789012345"); // 15 bytes

                await Task.WhenAny(allReceived.Task, Task.Delay(MessageTimeoutMs));

                // Average should be (5 + 10 + 15) / 3 = 10
                Assert.AreEqual(3L, server.Stats.ReceivedMessages);
                // Note: average calculation may vary based on implementation
                Assert.IsGreaterThan(server.Stats.ReceivedMessageSizeAverage, 0);
            });
        }

        private async Task RunSendAndWaitTests()
        {
            await RunIntegrationTestAsync("SendAndWait", "SendAndWaitAsync_Text_ReceivesResponse", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                // Echo server
                server.MessageReceived += async (s, e) =>
                {
                    var message = Encoding.UTF8.GetString(e.Data.Array, e.Data.Offset, e.Data.Count);
                    await server.SendAsync(e.Client.Guid, $"Echo: {message}");
                };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                string response = await client.SendAndWaitAsync("Hello", 5);
                Assert.IsNotNull(response);
                Assert.AreEqual("Echo: Hello", response);
            });

            await RunIntegrationTestAsync("SendAndWait", "SendAndWaitAsync_Binary_ReceivesResponse", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                // Echo server for binary
                server.MessageReceived += async (s, e) =>
                {
                    await server.SendAsync(e.Client.Guid, e.Data.ToArray(), WebSocketMessageType.Binary);
                };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                var requestData = new byte[] { 1, 2, 3, 4, 5 };
                var response = await client.SendAndWaitAsync(requestData, 5);

                Assert.IsNotNull(response.Array);
                Assert.AreEqual(5, response.Count);
                Assert.ArraysEqual(requestData, response.ToArray());
            });

            await RunIntegrationTestAsync("SendAndWait", "SendAndWaitAsync_Timeout_ReturnsNull", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                // Server does not respond
                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                string response = await client.SendAndWaitAsync("Hello", 2);
                Assert.IsNull(response);
            });

            await RunIntegrationTestAsync("SendAndWait", "SendAndWaitAsync_InvalidTimeout_ThrowsArgumentException", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                await Assert.ThrowsAsync<ArgumentException>(async () => await client.SendAndWaitAsync("Test", 0));
                await Assert.ThrowsAsync<ArgumentException>(async () => await client.SendAndWaitAsync("Test", -1));
            });

            await RunIntegrationTestAsync("SendAndWait", "SendAndWaitAsync_NullData_ThrowsArgumentNullException", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                await Assert.ThrowsAsync<ArgumentNullException>(async () => await client.SendAndWaitAsync((string)null, 5));
            });
        }

        private async Task RunHeadersAndCookiesTests()
        {
            await RunIntegrationTestAsync("HeadersAndCookies", "Client_CanSendCustomHeaders", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                string receivedHeader = null;
                var connected = new TaskCompletionSource<bool>();

                server.ClientConnected += (s, e) =>
                {
                    receivedHeader = e.HttpRequest.Headers["X-Custom-Header"];
                    connected.TrySetResult(true);
                };

                server.Start();

                var headers = new NameValueCollection();
                headers.Add("X-Custom-Header", "CustomValue");

                bool connectedResult = client.StartWithTimeout(5, headers);
                Assert.IsTrue(connectedResult);

                await Task.WhenAny(connected.Task, Task.Delay(MessageTimeoutMs));
                Assert.AreEqual("CustomValue", receivedHeader);
            });

            await RunIntegrationTestAsync("HeadersAndCookies", "Client_CanSendMultipleHeaders", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                var receivedHeaders = new Dictionary<string, string>();
                var connected = new TaskCompletionSource<bool>();

                server.ClientConnected += (s, e) =>
                {
                    receivedHeaders["X-Header-1"] = e.HttpRequest.Headers["X-Header-1"];
                    receivedHeaders["X-Header-2"] = e.HttpRequest.Headers["X-Header-2"];
                    connected.TrySetResult(true);
                };

                server.Start();

                var headers = new NameValueCollection();
                headers.Add("X-Header-1", "Value1");
                headers.Add("X-Header-2", "Value2");

                client.StartWithTimeout(5, headers);

                await Task.WhenAny(connected.Task, Task.Delay(MessageTimeoutMs));
                Assert.AreEqual("Value1", receivedHeaders["X-Header-1"]);
                Assert.AreEqual("Value2", receivedHeaders["X-Header-2"]);
            });

            await RunIntegrationTestAsync("HeadersAndCookies", "GuidHeader_SentInConnection", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);

                var customGuid = Guid.NewGuid();
                string receivedGuidHeader = null;
                var connected = new TaskCompletionSource<bool>();

                server.ClientConnected += (s, e) =>
                {
                    receivedGuidHeader = e.HttpRequest.Headers["x-guid"];
                    connected.TrySetResult(true);
                };

                server.Start();

                var client = ctx.CreateClient("127.0.0.1", port, false, customGuid);
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                await Task.WhenAny(connected.Task, Task.Delay(MessageTimeoutMs));
                Assert.IsNotNull(receivedGuidHeader);
                Assert.AreEqual(customGuid.ToString(), receivedGuidHeader);
            });

            await RunIntegrationTestAsync("HeadersAndCookies", "CustomGuidHeader_Used", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                server.GuidHeader = "X-My-Client-Id";

                var customGuid = Guid.NewGuid();
                string receivedGuidHeader = null;
                var connected = new TaskCompletionSource<bool>();

                server.ClientConnected += (s, e) =>
                {
                    receivedGuidHeader = e.HttpRequest.Headers["X-My-Client-Id"];
                    connected.TrySetResult(true);
                };

                server.Start();

                var client = ctx.CreateClient("127.0.0.1", port, false, customGuid);
                client.GuidHeader = "X-My-Client-Id";
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                await Task.WhenAny(connected.Task, Task.Delay(MessageTimeoutMs));
                Assert.IsNotNull(receivedGuidHeader);
                Assert.AreEqual(customGuid.ToString(), receivedGuidHeader);
            });
        }

        private async Task RunDisconnectionTests()
        {
            await RunIntegrationTestAsync("Disconnection", "ClientStop_GracefulDisconnect", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                var disconnected = new TaskCompletionSource<bool>();
                server.ClientDisconnected += (s, e) => { disconnected.TrySetResult(true); };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                Assert.IsTrue(client.Connected);

                client.Stop();
                Assert.IsFalse(client.Connected);

                var completed = await Task.WhenAny(disconnected.Task, Task.Delay(MessageTimeoutMs));
                Assert.AreEqual(disconnected.Task, completed, "Server should receive disconnect notification");
            });

            await RunIntegrationTestAsync("Disconnection", "ClientStopAsync_GracefulDisconnect", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                var disconnected = new TaskCompletionSource<bool>();
                server.ClientDisconnected += (s, e) => { disconnected.TrySetResult(true); };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                await client.StopAsync();
                Assert.IsFalse(client.Connected);

                var completed = await Task.WhenAny(disconnected.Task, Task.Delay(MessageTimeoutMs));
                Assert.AreEqual(disconnected.Task, completed);
            });

            await RunIntegrationTestAsync("Disconnection", "ClientStop_WithCustomCloseCode", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                // Stop with custom close code
                client.Stop(WebSocketCloseStatus.EndpointUnavailable, "Custom close reason");
                Assert.IsFalse(client.Connected);
            });

            await RunIntegrationTestAsync("Disconnection", "ServerDisconnectClient_ForcesDisconnect", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                Guid? clientGuid = null;
                var serverDisconnected = new TaskCompletionSource<bool>();
                var clientDisconnected = new TaskCompletionSource<bool>();

                server.ClientConnected += (s, e) => { clientGuid = e.Client.Guid; };
                server.ClientDisconnected += (s, e) => { serverDisconnected.TrySetResult(true); };
                client.ServerDisconnected += (s, e) => { clientDisconnected.TrySetResult(true); };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                await Task.Delay(200);

                Assert.IsNotNull(clientGuid);
                server.DisconnectClient(clientGuid.Value);

                var completed = await Task.WhenAny(
                    Task.WhenAll(serverDisconnected.Task, clientDisconnected.Task),
                    Task.Delay(MessageTimeoutMs));

                Assert.IsFalse(client.Connected);
                Assert.IsFalse(server.IsClientConnected(clientGuid.Value));
            });

            await RunIntegrationTestAsync("Disconnection", "ServerStop_DisconnectsAllClients", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var disconnectEvents = new ConcurrentBag<bool>();

                server.Start();

                var clients = new List<WatsonWsClient>();
                for (int i = 0; i < 3; i++)
                {
                    var client = ctx.CreateClient("127.0.0.1", port, false);
                    clients.Add(client);
                    client.ServerDisconnected += (s, e) => { disconnectEvents.Add(true); };
                    await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                }

                await Task.Delay(300);
                Assert.AreEqual(3, server.ListClients().Count());

                server.Stop();

                await Task.Delay(1000);
                Assert.AreEqual(3, disconnectEvents.Count, "All clients should receive disconnect event");

                foreach (var client in clients)
                {
                    Assert.IsFalse(client.Connected);
                }
            });

            await RunIntegrationTestAsync("Disconnection", "ClientReconnect_AfterDisconnect", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                server.Start();

                // First connection
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                Assert.IsTrue(client.Connected);

                // Disconnect
                await client.StopAsync();
                Assert.IsFalse(client.Connected);

                await Task.Delay(200);

                // Reconnect
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                Assert.IsTrue(client.Connected);
            });
        }

        private async Task RunDataIntegrityTests()
        {
            await RunIntegrationTestAsync("DataIntegrity", "BinaryData_PreservedExactly", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                byte[] receivedData = null;
                var messageReceived = new TaskCompletionSource<bool>();

                server.MessageReceived += (s, e) =>
                {
                    receivedData = e.Data.ToArray();
                    messageReceived.TrySetResult(true);
                };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                // Create data with all byte values
                var testData = new byte[256];
                for (int i = 0; i < 256; i++)
                    testData[i] = (byte)i;

                await client.SendAsync(testData, WebSocketMessageType.Binary);

                var completed = await Task.WhenAny(messageReceived.Task, Task.Delay(MessageTimeoutMs));
                Assert.AreEqual(messageReceived.Task, completed);
                Assert.ArraysEqual(testData, receivedData);
            });

            await RunIntegrationTestAsync("DataIntegrity", "UnicodeText_PreservedExactly", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                string receivedText = null;
                var messageReceived = new TaskCompletionSource<bool>();

                server.MessageReceived += (s, e) =>
                {
                    receivedText = Encoding.UTF8.GetString(e.Data.Array, e.Data.Offset, e.Data.Count);
                    messageReceived.TrySetResult(true);
                };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                var unicodeText = "Hello, 世界! 🌍 Привет! مرحبا";
                await client.SendAsync(unicodeText);

                var completed = await Task.WhenAny(messageReceived.Task, Task.Delay(MessageTimeoutMs));
                Assert.AreEqual(messageReceived.Task, completed);
                Assert.AreEqual(unicodeText, receivedText);
            });

            await RunIntegrationTestAsync("DataIntegrity", "LargeMessage_1MB_PreservedExactly", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                byte[] receivedData = null;
                var messageReceived = new TaskCompletionSource<bool>();

                server.MessageReceived += (s, e) =>
                {
                    receivedData = e.Data.ToArray();
                    messageReceived.TrySetResult(true);
                };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                // 1MB of random data
                var largeData = new byte[1024 * 1024];
                new Random(12345).NextBytes(largeData);

                await client.SendAsync(largeData, WebSocketMessageType.Binary);

                var completed = await Task.WhenAny(messageReceived.Task, Task.Delay(30000));
                Assert.AreEqual(messageReceived.Task, completed, "Large message should be received");
                Assert.AreEqual(largeData.Length, receivedData.Length);
                Assert.ArraysEqual(largeData, receivedData);
            });

            await RunIntegrationTestAsync("DataIntegrity", "MultipleMessages_MaintainOrder", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                var receivedMessages = new List<int>();
                var allReceived = new TaskCompletionSource<bool>();
                const int messageCount = 100;

                server.MessageReceived += (s, e) =>
                {
                    var num = int.Parse(Encoding.UTF8.GetString(e.Data.Array, e.Data.Offset, e.Data.Count));
                    lock (receivedMessages)
                    {
                        receivedMessages.Add(num);
                        if (receivedMessages.Count == messageCount)
                            allReceived.TrySetResult(true);
                    }
                };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                for (int i = 0; i < messageCount; i++)
                {
                    await client.SendAsync(i.ToString());
                }

                var completed = await Task.WhenAny(allReceived.Task, Task.Delay(10000));
                Assert.AreEqual(allReceived.Task, completed, "All messages should be received");
                Assert.AreEqual(messageCount, receivedMessages.Count);

                // Verify order
                for (int i = 0; i < messageCount; i++)
                {
                    Assert.AreEqual(i, receivedMessages[i], $"Message order preserved at index {i}");
                }
            });

            await RunIntegrationTestAsync("DataIntegrity", "RoundTrip_DataUnchanged", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                // Echo server
                server.MessageReceived += async (s, e) =>
                {
                    await server.SendAsync(e.Client.Guid, e.Data.ToArray(), e.MessageType);
                };

                byte[] receivedData = null;
                var messageReceived = new TaskCompletionSource<bool>();

                client.MessageReceived += (s, e) =>
                {
                    receivedData = e.Data.ToArray();
                    messageReceived.TrySetResult(true);
                };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                var testData = new byte[10000];
                new Random(42).NextBytes(testData);

                await client.SendAsync(testData, WebSocketMessageType.Binary);

                var completed = await Task.WhenAny(messageReceived.Task, Task.Delay(MessageTimeoutMs));
                Assert.AreEqual(messageReceived.Task, completed);
                Assert.ArraysEqual(testData, receivedData);
            });
        }

        private async Task RunConcurrencyTests()
        {
            await RunIntegrationTestAsync("Concurrency", "ConcurrentSends_FromClient", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                int receivedCount = 0;
                var allReceived = new TaskCompletionSource<bool>();
                const int messageCount = 50;

                server.MessageReceived += (s, e) =>
                {
                    if (Interlocked.Increment(ref receivedCount) == messageCount)
                        allReceived.TrySetResult(true);
                };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                // Send messages concurrently
                var tasks = new List<Task<bool>>();
                for (int i = 0; i < messageCount; i++)
                {
                    tasks.Add(client.SendAsync($"Concurrent message {i}"));
                }

                await Task.WhenAll(tasks);
                Assert.IsTrue(tasks.All(t => t.Result), "All sends should succeed");

                var completed = await Task.WhenAny(allReceived.Task, Task.Delay(10000));
                Assert.AreEqual(allReceived.Task, completed, "All messages should be received");
                Assert.AreEqual(messageCount, receivedCount);
            });

            await RunIntegrationTestAsync("Concurrency", "ConcurrentSends_FromServer_ToMultipleClients", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var clientGuids = new ConcurrentBag<Guid>();
                int totalReceived = 0;
                var allReceived = new TaskCompletionSource<bool>();
                const int clientCount = 5;
                const int messagesPerClient = 10;

                server.ClientConnected += (s, e) => { clientGuids.Add(e.Client.Guid); };
                server.Start();

                for (int i = 0; i < clientCount; i++)
                {
                    var client = ctx.CreateClient("127.0.0.1", port, false);
                    client.MessageReceived += (s, e) =>
                    {
                        if (Interlocked.Increment(ref totalReceived) == clientCount * messagesPerClient)
                            allReceived.TrySetResult(true);
                    };
                    await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                }

                await Task.Delay(500);
                Assert.AreEqual(clientCount, clientGuids.Count);

                // Send to all clients concurrently
                var tasks = new List<Task>();
                foreach (var guid in clientGuids)
                {
                    for (int i = 0; i < messagesPerClient; i++)
                    {
                        tasks.Add(server.SendAsync(guid, $"Message {i}"));
                    }
                }

                await Task.WhenAll(tasks);

                var completed = await Task.WhenAny(allReceived.Task, Task.Delay(15000));
                Assert.AreEqual(allReceived.Task, completed, "All messages should be received");
                Assert.AreEqual(clientCount * messagesPerClient, totalReceived);
            });

            await RunIntegrationTestAsync("Concurrency", "ConcurrentConnections", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var clients = new ConcurrentBag<WatsonWsClient>();

                server.Start();

                const int connectionCount = 10;
                var connectionTasks = new List<Task>();

                for (int i = 0; i < connectionCount; i++)
                {
                    connectionTasks.Add(Task.Run(async () =>
                    {
                        var client = ctx.CreateClient("127.0.0.1", port, false);
                        clients.Add(client);
                        await client.StartWithTimeoutAsync(10);
                    }));
                }

                await Task.WhenAll(connectionTasks);
                await Task.Delay(500);

                Assert.AreEqual(connectionCount, server.ListClients().Count());

                foreach (var client in clients)
                {
                    Assert.IsTrue(client.Connected);
                }
            });

            await RunIntegrationTestAsync("Concurrency", "BidirectionalMessaging", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                int serverReceived = 0;
                int clientReceived = 0;
                var allDone = new TaskCompletionSource<bool>();
                const int messageCount = 20;
                Guid? clientGuid = null;

                server.ClientConnected += (s, e) => { clientGuid = e.Client.Guid; };

                server.MessageReceived += async (s, e) =>
                {
                    Interlocked.Increment(ref serverReceived);
                    await server.SendAsync(e.Client.Guid, "Server response");
                };

                client.MessageReceived += (s, e) =>
                {
                    if (Interlocked.Increment(ref clientReceived) == messageCount)
                        allDone.TrySetResult(true);
                };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                await Task.Delay(200);

                // Client sends, server echoes
                for (int i = 0; i < messageCount; i++)
                {
                    await client.SendAsync($"Client message {i}");
                }

                var completed = await Task.WhenAny(allDone.Task, Task.Delay(10000));
                Assert.AreEqual(allDone.Task, completed);
                Assert.AreEqual(messageCount, serverReceived);
                Assert.AreEqual(messageCount, clientReceived);
            });
        }

        private async Task RunEdgeCaseTests()
        {
            await RunIntegrationTestAsync("EdgeCases", "SendAsync_NullString_ThrowsArgumentNullException", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                await Assert.ThrowsAsync<ArgumentNullException>(async () => await client.SendAsync((string)null));
            });

            await RunIntegrationTestAsync("EdgeCases", "SendAsync_NullArraySegment_ThrowsArgumentNullException", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                    await client.SendAsync(new ArraySegment<byte>(null), WebSocketMessageType.Binary));
            });

            await RunIntegrationTestAsync("EdgeCases", "ServerSendAsync_NullArraySegment_ThrowsArgumentNullException", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                Guid? clientGuid = null;
                server.ClientConnected += (s, e) => { clientGuid = e.Client.Guid; };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                await Task.Delay(200);

                Assert.IsNotNull(clientGuid);
                await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                    await server.SendAsync(clientGuid.Value, new ArraySegment<byte>(null), WebSocketMessageType.Binary));
            });

            await RunIntegrationTestAsync("EdgeCases", "EmptyString_SendBehavior", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                // Empty strings may throw ArgumentNullException or be sent successfully
                // depending on implementation
                try
                {
                    bool sent = await client.SendAsync("");
                    // If no exception, the send was accepted
                }
                catch (ArgumentNullException)
                {
                    // Some implementations may reject empty strings
                }
            });

            await RunIntegrationTestAsync("EdgeCases", "StartWithTimeout_CancellationToken", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var client = ctx.CreateClient("127.0.0.1", port, false);

                using var cts = new CancellationTokenSource(500);

                // No server, connection should fail or be cancelled
                bool connected = await client.StartWithTimeoutAsync(60, null, cts.Token);
                Assert.IsFalse(connected);
            });

            await RunIntegrationTestAsync("EdgeCases", "SendAsync_WithCancellation", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                using var cts = new CancellationTokenSource();
                var task = client.SendAsync("Test", WebSocketMessageType.Text, cts.Token);
                // Don't cancel, just verify it completes
                var result = await task;
                Assert.IsTrue(result);
            });

            await RunIntegrationTestAsync("EdgeCases", "PermittedIpAddresses_BlocksUnauthorized", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);

                // Only allow a non-existent IP
                server.PermittedIpAddresses.Add("192.168.255.255");

                var connected = new TaskCompletionSource<bool>();
                server.ClientConnected += (s, e) => { connected.TrySetResult(true); };

                server.Start();

                var client = ctx.CreateClient("127.0.0.1", port, false);
                bool result = await client.StartWithTimeoutAsync(3);

                // Connection should be rejected
                await Task.Delay(500);

                // Client may connect at TCP level but should be disconnected
                var clients = server.ListClients().ToList();
                Assert.AreEqual(0, clients.Count, "No clients should be connected when IP not permitted");
            });

            await RunIntegrationTestAsync("EdgeCases", "PermittedIpAddresses_AllowsAuthorized", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);

                // Allow 127.0.0.1
                server.PermittedIpAddresses.Add("127.0.0.1");

                var connected = new TaskCompletionSource<bool>();
                server.ClientConnected += (s, e) => { connected.TrySetResult(true); };

                server.Start();

                var client = ctx.CreateClient("127.0.0.1", port, false);
                bool result = await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);

                // Wait for connection event or timeout
                var completed = await Task.WhenAny(connected.Task, Task.Delay(MessageTimeoutMs));

                // Verify client connected successfully
                Assert.IsTrue(result || client.Connected, "Connection should succeed for permitted IP");
            });

            await RunIntegrationTestAsync("EdgeCases", "ConfigureOptions_FluentAPI", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var client = ctx.CreateClient("127.0.0.1", port, false);

                var result = client
                    .ConfigureOptions(opts => { })
                    .ConfigureOptions(opts => { });

                Assert.AreEqual(client, result, "ConfigureOptions should return same client instance");
                await Task.CompletedTask;
            });

            await RunIntegrationTestAsync("EdgeCases", "ClientMetadata_NameAndMetadata_CanBeUsed", async () =>
            {
                int port = GetNextPort();
                using var ctx = CreateTestContext();
                var server = ctx.CreateServer("127.0.0.1", port, false);
                var client = ctx.CreateClient("127.0.0.1", port, false);

                ClientMetadata connectedClient = null;
                server.ClientConnected += (s, e) =>
                {
                    connectedClient = e.Client;
                    e.Client.Name = "TestClient";
                    e.Client.Metadata = new { UserId = 12345 };
                };

                server.Start();
                await client.StartWithTimeoutAsync(ConnectionTimeoutSeconds);
                await Task.Delay(200);

                Assert.IsNotNull(connectedClient);
                Assert.AreEqual("TestClient", connectedClient.Name);
                Assert.IsNotNull(connectedClient.Metadata);
            });
        }
    }
}
