using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using WatsonWebsocket;

namespace Test.Automated.Tests
{
    public class ClientTests
    {
        private readonly TestRunner _runner;
        private int _basePort = 11000;

        public ClientTests(TestRunner runner)
        {
            _runner = runner;
        }

        private int GetNextPort() => Interlocked.Increment(ref _basePort);

        public async Task RunAllTests()
        {
            Console.WriteLine("\n--- Client Constructor Tests ---\n");
            await RunConstructorTests();

            Console.WriteLine("\n--- Client Property Tests ---\n");
            await RunPropertyTests();

            Console.WriteLine("\n--- Client Lifecycle Tests ---\n");
            await RunLifecycleTests();
        }

        private async Task RunConstructorTests()
        {
            await _runner.RunTestAsync("ClientConstructor", "ConstructorWithIpPortSsl_ValidParameters", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);
                Assert.IsNotNull(client);
                Assert.IsFalse(client.Connected);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientConstructor", "ConstructorWithHostname", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);
                Assert.IsNotNull(client);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientConstructor", "ConstructorWithSsl", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, true);
                Assert.IsNotNull(client);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientConstructor", "ConstructorWithGuid", async () =>
            {
                int port = GetNextPort();
                var customGuid = Guid.NewGuid();
                using var client = new WatsonWsClient("127.0.0.1", port, false, customGuid);
                Assert.IsNotNull(client);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientConstructor", "ConstructorWithNullIp_ThrowsArgumentNullException", async () =>
            {
                int port = GetNextPort();
                Assert.Throws<ArgumentNullException>(() => new WatsonWsClient(null, port, false));
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientConstructor", "ConstructorWithEmptyIp_ThrowsArgumentNullException", async () =>
            {
                int port = GetNextPort();
                Assert.Throws<ArgumentNullException>(() => new WatsonWsClient("", port, false));
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientConstructor", "ConstructorWithInvalidPort_Zero_ThrowsArgumentOutOfRangeException", async () =>
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new WatsonWsClient("127.0.0.1", 0, false));
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientConstructor", "ConstructorWithInvalidPort_Negative_ThrowsArgumentOutOfRangeException", async () =>
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new WatsonWsClient("127.0.0.1", -1, false));
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientConstructor", "ConstructorWithInvalidPort_TooHigh_ThrowsException", async () =>
            {
                // Ports above 65535 may throw ArgumentOutOfRangeException or UriFormatException
                bool threwException = false;
                try
                {
                    new WatsonWsClient("127.0.0.1", 65536, false);
                }
                catch (ArgumentOutOfRangeException)
                {
                    threwException = true;
                }
                catch (UriFormatException)
                {
                    threwException = true;
                }
                Assert.IsTrue(threwException, "Should throw exception for invalid port");
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientConstructor", "ConstructorWithUri_Http", async () =>
            {
                int port = GetNextPort();
                var uri = new Uri($"ws://127.0.0.1:{port}");
                using var client = new WatsonWsClient(uri);
                Assert.IsNotNull(client);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientConstructor", "ConstructorWithUri_Https", async () =>
            {
                int port = GetNextPort();
                var uri = new Uri($"wss://127.0.0.1:{port}");
                using var client = new WatsonWsClient(uri);
                Assert.IsNotNull(client);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientConstructor", "ConstructorWithUri_WithPath", async () =>
            {
                int port = GetNextPort();
                var uri = new Uri($"ws://127.0.0.1:{port}/api/websocket");
                using var client = new WatsonWsClient(uri);
                Assert.IsNotNull(client);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientConstructor", "ConstructorWithUri_WithGuid", async () =>
            {
                int port = GetNextPort();
                var uri = new Uri($"ws://127.0.0.1:{port}");
                var customGuid = Guid.NewGuid();
                using var client = new WatsonWsClient(uri, customGuid);
                Assert.IsNotNull(client);
                await Task.CompletedTask;
            });
        }

        private async Task RunPropertyTests()
        {
            await _runner.RunTestAsync("ClientProperties", "Connected_InitiallyFalse", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);
                Assert.IsFalse(client.Connected);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientProperties", "EnableStatistics_DefaultTrue", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);
                Assert.IsTrue(client.EnableStatistics);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientProperties", "EnableStatistics_CanBeToggled", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);
                client.EnableStatistics = false;
                Assert.IsFalse(client.EnableStatistics);
                client.EnableStatistics = true;
                Assert.IsTrue(client.EnableStatistics);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientProperties", "AcceptInvalidCertificates_DefaultTrue", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);
                Assert.IsTrue(client.AcceptInvalidCertificates);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientProperties", "AcceptInvalidCertificates_CanBeToggled", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);
                client.AcceptInvalidCertificates = false;
                Assert.IsFalse(client.AcceptInvalidCertificates);
                client.AcceptInvalidCertificates = true;
                Assert.IsTrue(client.AcceptInvalidCertificates);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientProperties", "KeepAliveInterval_DefaultValue", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);
                Assert.AreEqual(30, client.KeepAliveInterval);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientProperties", "KeepAliveInterval_CanBeModified", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);
                client.KeepAliveInterval = 60;
                Assert.AreEqual(60, client.KeepAliveInterval);
                client.KeepAliveInterval = 10;
                Assert.AreEqual(10, client.KeepAliveInterval);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientProperties", "Logger_DefaultNull", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);
                Assert.IsNull(client.Logger);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientProperties", "Logger_CanBeSet", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);
                bool loggerCalled = false;
                client.Logger = (msg) => { loggerCalled = true; };
                Assert.IsNotNull(client.Logger);
                client.Logger("test");
                Assert.IsTrue(loggerCalled);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientProperties", "GuidHeader_DefaultValue", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);
                Assert.AreEqual("x-guid", client.GuidHeader);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientProperties", "GuidHeader_CanBeModified", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);
                client.GuidHeader = "X-My-Guid";
                Assert.AreEqual("X-My-Guid", client.GuidHeader);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientProperties", "Stats_IsNotNull", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);
                Assert.IsNotNull(client.Stats);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientProperties", "Stats_InitialValues", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);
                Assert.AreEqual(0L, client.Stats.ReceivedBytes);
                Assert.AreEqual(0L, client.Stats.SentBytes);
                Assert.AreEqual(0L, client.Stats.ReceivedMessages);
                Assert.AreEqual(0L, client.Stats.SentMessages);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientProperties", "ConfigureOptions_ReturnsSelf", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);
                var result = client.ConfigureOptions(opts => { });
                Assert.AreEqual(client, result);
                await Task.CompletedTask;
            });
        }

        private async Task RunLifecycleTests()
        {
            await _runner.RunTestAsync("ClientLifecycle", "Start_ToNonexistentServer_HandlesGracefully", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);

                // Starting connection to non-existent server should not crash
                // It may throw or set Connected to false
                try
                {
                    client.Start();
                    await Task.Delay(500); // Give time for connection attempt
                }
                catch
                {
                    // Expected - no server running
                }

                Assert.IsFalse(client.Connected);
            });

            await _runner.RunTestAsync("ClientLifecycle", "StartWithTimeout_ToNonexistentServer_ReturnsFalse", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);

                bool connected = client.StartWithTimeout(2);
                Assert.IsFalse(connected);
                Assert.IsFalse(client.Connected);
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientLifecycle", "StartWithTimeoutAsync_ToNonexistentServer_ReturnsFalse", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);

                bool connected = await client.StartWithTimeoutAsync(2);
                Assert.IsFalse(connected);
                Assert.IsFalse(client.Connected);
            });

            await _runner.RunTestAsync("ClientLifecycle", "StartWithTimeout_InvalidTimeout_ThrowsArgumentException", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);

                Assert.Throws<ArgumentException>(() => client.StartWithTimeout(0));
                Assert.Throws<ArgumentException>(() => client.StartWithTimeout(-1));
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientLifecycle", "StartWithTimeoutAsync_InvalidTimeout_ThrowsArgumentException", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);

                await Assert.ThrowsAsync<ArgumentException>(async () => await client.StartWithTimeoutAsync(0));
                await Assert.ThrowsAsync<ArgumentException>(async () => await client.StartWithTimeoutAsync(-1));
            });

            await _runner.RunTestAsync("ClientLifecycle", "Dispose_CanBeCalledMultipleTimes", async () =>
            {
                int port = GetNextPort();
                var client = new WatsonWsClient("127.0.0.1", port, false);
                client.Dispose();
                Assert.DoesNotThrow(() => client.Dispose());
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientLifecycle", "Stop_WhenNotConnected_ThrowsInvalidOperationException", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);
                // Stop throws InvalidOperationException when not connected
                Assert.Throws<InvalidOperationException>(() => client.Stop());
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientLifecycle", "StopAsync_WhenNotConnected_ThrowsInvalidOperationException", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);
                // StopAsync throws InvalidOperationException when not connected
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.StopAsync());
            });

            await _runner.RunTestAsync("ClientLifecycle", "Stop_WithCloseCode_WhenNotConnected_ThrowsInvalidOperationException", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);
                // Stop with close code throws InvalidOperationException when not connected
                Assert.Throws<InvalidOperationException>(() => client.Stop(WebSocketCloseStatus.NormalClosure, "Test close"));
                await Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientLifecycle", "StopAsync_WithCloseCode_WhenNotConnected_ThrowsInvalidOperationException", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);
                // StopAsync with close code throws InvalidOperationException when not connected
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.StopAsync(WebSocketCloseStatus.NormalClosure, "Test close"));
            });

            await _runner.RunTestAsync("ClientLifecycle", "SendAsync_WhenNotConnected_ReturnsFalse", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);

                bool result = await client.SendAsync("Test message");
                Assert.IsFalse(result);
            });

            await _runner.RunTestAsync("ClientLifecycle", "SendAsync_Bytes_WhenNotConnected_ReturnsFalse", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);

                bool result = await client.SendAsync(new byte[] { 1, 2, 3 });
                Assert.IsFalse(result);
            });

            await _runner.RunTestAsync("ClientLifecycle", "SendAsync_ArraySegment_WhenNotConnected_ReturnsFalse", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);

                var segment = new ArraySegment<byte>(new byte[] { 1, 2, 3 });
                bool result = await client.SendAsync(segment);
                Assert.IsFalse(result);
            });

            await _runner.RunTestAsync("ClientLifecycle", "AddCookie_BeforeConnection", async () =>
            {
                int port = GetNextPort();
                using var client = new WatsonWsClient("127.0.0.1", port, false);

                // Cookie requires a domain to be set
                var cookie = new Cookie("TestCookie", "TestValue", "/", "127.0.0.1");
                Assert.DoesNotThrow(() => client.AddCookie(cookie));
                await Task.CompletedTask;
            });
        }
    }
}
