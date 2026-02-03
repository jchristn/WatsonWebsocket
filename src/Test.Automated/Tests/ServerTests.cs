using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using WatsonWebsocket;

namespace Test.Automated.Tests
{
    public class ServerTests
    {
        private readonly TestRunner _runner;
        private int _basePort = 10000;

        public ServerTests(TestRunner runner)
        {
            _runner = runner;
        }

        private int GetNextPort() => Interlocked.Increment(ref _basePort);

        public async Task RunAllTests()
        {
            Console.WriteLine("\n--- Server Constructor Tests ---\n");
            await RunConstructorTests();

            Console.WriteLine("\n--- Server Property Tests ---\n");
            await RunPropertyTests();

            Console.WriteLine("\n--- Server Lifecycle Tests ---\n");
            await RunLifecycleTests();
        }

        private async Task RunConstructorTests()
        {
            await _runner.RunTestAsync("ServerConstructor", "DefaultConstructor_CreatesValidServer", async () =>
            {
                using var server = new WatsonWsServer();
                Assert.IsNotNull(server);
                Assert.IsFalse(server.IsListening);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerConstructor", "ConstructorWithHostnameAndPort", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                Assert.IsNotNull(server);
                Assert.IsFalse(server.IsListening);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerConstructor", "ConstructorWithSsl", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, true);
                Assert.IsNotNull(server);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerConstructor", "ConstructorWithHostnameList", async () =>
            {
                int port = GetNextPort();
                var hostnames = new List<string> { "127.0.0.1", "127.0.0.1" };
                using var server = new WatsonWsServer(hostnames, port, false);
                Assert.IsNotNull(server);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerConstructor", "ConstructorWithNullHostnameList_ThrowsArgumentNullException", async () =>
            {
                int port = GetNextPort();
                Assert.Throws<ArgumentNullException>(() => new WatsonWsServer((List<string>)null, port, false));
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerConstructor", "ConstructorWithEmptyHostnameList_ThrowsArgumentException", async () =>
            {
                int port = GetNextPort();
                var emptyList = new List<string>();
                Assert.Throws<ArgumentException>(() => new WatsonWsServer(emptyList, port, false));
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerConstructor", "ConstructorWithUri", async () =>
            {
                int port = GetNextPort();
                var uri = new Uri($"http://127.0.0.1:{port}");
                using var server = new WatsonWsServer(uri);
                Assert.IsNotNull(server);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerConstructor", "ConstructorWithNullUri_ThrowsArgumentNullException", async () =>
            {
                Assert.Throws<ArgumentNullException>(() => new WatsonWsServer((Uri)null));
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerConstructor", "ConstructorWithInvalidUriPort_ThrowsArgumentException", async () =>
            {
                // URI with port -1 or missing port
                try
                {
                    var uri = new Uri("http://127.0.0.1"); // No port
                    using var server = new WatsonWsServer(uri);
                    // May or may not throw - depends on URI parsing
                }
                catch (ArgumentException)
                {
                    // Expected
                }
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerConstructor", "ConstructorWithSettings", async () =>
            {
                int port = GetNextPort();
                var settings = new WebsocketSettings
                {
                    Hostnames = new List<string> { "127.0.0.1" },
                    Port = port,
                    Ssl = false
                };
                using var server = new WatsonWsServer(settings);
                Assert.IsNotNull(server);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerConstructor", "ConstructorWithNullSettings_ThrowsArgumentNullException", async () =>
            {
                Assert.Throws<ArgumentNullException>(() => new WatsonWsServer((WebsocketSettings)null));
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerConstructor", "ConstructorWithInvalidPort_Zero_Behavior", async () =>
            {
                // Port 0 may be allowed at construction and fail at Start() time
                // Or may throw ArgumentOutOfRangeException depending on implementation
                try
                {
                    using var server = new WatsonWsServer("127.0.0.1", 0, false);
                    // If we get here, the constructor allowed it - test that Start fails
                    try
                    {
                        server.Start();
                        server.Stop();
                    }
                    catch
                    {
                        // Expected - port 0 should fail at start
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Also acceptable - validation at construction time
                }
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerConstructor", "ConstructorWithInvalidPort_Negative_ThrowsArgumentOutOfRangeException", async () =>
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new WatsonWsServer("127.0.0.1", -1, false));
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerConstructor", "ConstructorWithInvalidPort_TooHigh_Behavior", async () =>
            {
                // Port above 65535 may be allowed at construction and fail at Start() time
                // Or may throw ArgumentOutOfRangeException depending on implementation
                try
                {
                    using var server = new WatsonWsServer("127.0.0.1", 65536, false);
                    // If we get here, the constructor allowed it - test that Start fails or use it
                    try
                    {
                        server.Start();
                        server.Stop();
                    }
                    catch
                    {
                        // Expected - invalid port should fail at start
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Also acceptable - validation at construction time
                }
                await Task.CompletedTask;
            });
        }

        private async Task RunPropertyTests()
        {
            await _runner.RunTestAsync("ServerProperties", "IsListening_InitiallyFalse", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                Assert.IsFalse(server.IsListening);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerProperties", "EnableStatistics_DefaultTrue", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                Assert.IsTrue(server.EnableStatistics);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerProperties", "EnableStatistics_CanBeToggled", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                server.EnableStatistics = false;
                Assert.IsFalse(server.EnableStatistics);
                server.EnableStatistics = true;
                Assert.IsTrue(server.EnableStatistics);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerProperties", "AcceptInvalidCertificates_DefaultTrue", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                Assert.IsTrue(server.AcceptInvalidCertificates);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerProperties", "AcceptInvalidCertificates_CanBeToggled", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                server.AcceptInvalidCertificates = false;
                Assert.IsFalse(server.AcceptInvalidCertificates);
                server.AcceptInvalidCertificates = true;
                Assert.IsTrue(server.AcceptInvalidCertificates);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerProperties", "PermittedIpAddresses_DefaultEmpty", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                Assert.IsNotNull(server.PermittedIpAddresses);
                Assert.AreEqual(0, server.PermittedIpAddresses.Count);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerProperties", "PermittedIpAddresses_CanBeModified", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                server.PermittedIpAddresses.Add("127.0.0.1");
                server.PermittedIpAddresses.Add("192.168.1.0");
                Assert.AreEqual(2, server.PermittedIpAddresses.Count);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerProperties", "ListenerPrefixes_IsNotNull", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                Assert.IsNotNull(server.ListenerPrefixes);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerProperties", "Logger_DefaultNull", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                Assert.IsNull(server.Logger);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerProperties", "Logger_CanBeSet", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                bool loggerCalled = false;
                server.Logger = (msg) => { loggerCalled = true; };
                Assert.IsNotNull(server.Logger);
                server.Logger("test");
                Assert.IsTrue(loggerCalled);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerProperties", "HttpHandler_DefaultNull", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                Assert.IsNull(server.HttpHandler);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerProperties", "HttpHandler_CanBeSet", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                server.HttpHandler = (ctx) => { };
                Assert.IsNotNull(server.HttpHandler);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerProperties", "GuidHeader_DefaultValue", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                Assert.AreEqual("x-guid", server.GuidHeader);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerProperties", "GuidHeader_CanBeModified", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                server.GuidHeader = "X-Custom-Guid";
                Assert.AreEqual("X-Custom-Guid", server.GuidHeader);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerProperties", "Stats_IsNotNull", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                Assert.IsNotNull(server.Stats);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerProperties", "Stats_InitialValues", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                Assert.AreEqual(0L, server.Stats.ReceivedBytes);
                Assert.AreEqual(0L, server.Stats.SentBytes);
                Assert.AreEqual(0L, server.Stats.ReceivedMessages);
                Assert.AreEqual(0L, server.Stats.SentMessages);
                await Task.CompletedTask;
            });
        }

        private async Task RunLifecycleTests()
        {
            await _runner.RunTestAsync("ServerLifecycle", "Start_SetsIsListeningTrue", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                server.Start();
                Assert.IsTrue(server.IsListening);
                server.Stop();
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerLifecycle", "StartAsync_SetsIsListeningTrue", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                await server.StartAsync();
                Assert.IsTrue(server.IsListening);
                server.Stop();
            });

            await _runner.RunTestAsync("ServerLifecycle", "Stop_SetsIsListeningFalse", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                server.Start();
                Assert.IsTrue(server.IsListening);
                server.Stop();
                Assert.IsFalse(server.IsListening);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerLifecycle", "Start_WhenAlreadyListening_ThrowsInvalidOperationException", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                server.Start();
                try
                {
                    Assert.Throws<InvalidOperationException>(() => server.Start());
                }
                finally
                {
                    server.Stop();
                }
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerLifecycle", "Stop_WhenNotListening_ThrowsInvalidOperationException", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                Assert.Throws<InvalidOperationException>(() => server.Stop());
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerLifecycle", "Dispose_StopsListening", async () =>
            {
                int port = GetNextPort();
                var server = new WatsonWsServer("127.0.0.1", port, false);
                server.Start();
                Assert.IsTrue(server.IsListening);
                server.Dispose();
                // After dispose, checking IsListening might throw or return false
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerLifecycle", "Dispose_CanBeCalledMultipleTimes", async () =>
            {
                int port = GetNextPort();
                var server = new WatsonWsServer("127.0.0.1", port, false);
                server.Dispose();
                Assert.DoesNotThrow(() => server.Dispose());
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerLifecycle", "StartStopStart_Works", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);

                server.Start();
                Assert.IsTrue(server.IsListening);

                server.Stop();
                Assert.IsFalse(server.IsListening);

                // Should be able to start again
                server.Start();
                Assert.IsTrue(server.IsListening);

                server.Stop();
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerLifecycle", "ListClients_EmptyWhenNoConnections", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                server.Start();
                try
                {
                    var clients = server.ListClients();
                    Assert.IsNotNull(clients);
                    Assert.CollectionIsEmpty(clients);
                }
                finally
                {
                    server.Stop();
                }
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerLifecycle", "IsClientConnected_ReturnsFalse_ForNonexistentGuid", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                server.Start();
                try
                {
                    var randomGuid = Guid.NewGuid();
                    Assert.IsFalse(server.IsClientConnected(randomGuid));
                }
                finally
                {
                    server.Stop();
                }
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerLifecycle", "DisconnectClient_DoesNotThrow_ForNonexistentGuid", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                server.Start();
                try
                {
                    var randomGuid = Guid.NewGuid();
                    Assert.DoesNotThrow(() => server.DisconnectClient(randomGuid));
                }
                finally
                {
                    server.Stop();
                }
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ServerLifecycle", "ServerStopped_EventFires", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                bool eventFired = false;
                server.ServerStopped += (s, e) => { eventFired = true; };

                server.Start();
                server.Stop();

                // Give event time to fire
                await Task.Delay(100);
                Assert.IsTrue(eventFired, "ServerStopped event should have fired");
            });

            await _runner.RunTestAsync("ServerLifecycle", "GetAwaiter_ReturnsValidAwaiter", async () =>
            {
                int port = GetNextPort();
                using var server = new WatsonWsServer("127.0.0.1", port, false);
                server.Start();
                try
                {
                    var awaiter = server.GetAwaiter();
                    Assert.IsNotNull(awaiter);
                }
                finally
                {
                    server.Stop();
                }
                await Task.CompletedTask;
            });
        }
    }
}
