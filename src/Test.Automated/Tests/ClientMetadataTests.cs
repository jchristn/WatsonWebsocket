using System;
using System.Threading.Tasks;
using WatsonWebsocket;

namespace Test.Automated.Tests
{
    public class ClientMetadataTests
    {
        private readonly TestRunner _runner;

        public ClientMetadataTests(TestRunner runner)
        {
            _runner = runner;
        }

        public async Task RunAllTests()
        {
            Console.WriteLine("\n--- ClientMetadata Tests ---\n");

            await _runner.RunTestAsync("ClientMetadata", "DefaultConstructor_InitializesCorrectly", () =>
            {
                var metadata = new ClientMetadata();
                Assert.AreNotEqual(Guid.Empty, metadata.Guid, "Guid should be auto-generated");
                Assert.IsNull(metadata.Ip);
                Assert.AreEqual(0, metadata.Port);
                Assert.IsNull(metadata.Name);
                Assert.IsNull(metadata.Metadata);
                return Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientMetadata", "Guid_CanBeSet", () =>
            {
                var metadata = new ClientMetadata();
                var customGuid = Guid.NewGuid();
                metadata.Guid = customGuid;
                Assert.AreEqual(customGuid, metadata.Guid);
                return Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientMetadata", "Ip_CanBeSet", () =>
            {
                var metadata = new ClientMetadata();
                metadata.Ip = "192.168.1.100";
                Assert.AreEqual("192.168.1.100", metadata.Ip);
                return Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientMetadata", "Port_ValidValue_CanBeSet", () =>
            {
                var metadata = new ClientMetadata();
                metadata.Port = 8080;
                Assert.AreEqual(8080, metadata.Port);
                metadata.Port = 0;
                Assert.AreEqual(0, metadata.Port);
                metadata.Port = 65535;
                Assert.AreEqual(65535, metadata.Port);
                return Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientMetadata", "Port_NegativeValue_ThrowsArgumentOutOfRangeException", () =>
            {
                var metadata = new ClientMetadata();
                Assert.Throws<ArgumentOutOfRangeException>(() => metadata.Port = -1);
                Assert.Throws<ArgumentOutOfRangeException>(() => metadata.Port = -100);
                return Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientMetadata", "Name_CanBeSet", () =>
            {
                var metadata = new ClientMetadata();
                metadata.Name = "TestClient";
                Assert.AreEqual("TestClient", metadata.Name);
                metadata.Name = null;
                Assert.IsNull(metadata.Name);
                metadata.Name = "";
                Assert.AreEqual("", metadata.Name);
                return Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientMetadata", "Metadata_CanBeSet", () =>
            {
                var metadata = new ClientMetadata();
                var customData = new { Id = 123, Type = "Test" };
                metadata.Metadata = customData;
                Assert.AreEqual(customData, metadata.Metadata);
                return Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientMetadata", "IpPort_ComputedCorrectly", () =>
            {
                var metadata = new ClientMetadata();
                metadata.Ip = "10.0.0.1";
                metadata.Port = 9000;
                Assert.AreEqual("10.0.0.1:9000", metadata.IpPort);
                return Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientMetadata", "IpPort_WithNullIp", () =>
            {
                var metadata = new ClientMetadata();
                metadata.Ip = null;
                metadata.Port = 9000;
                // Should be ":9000" or handle gracefully
                var ipPort = metadata.IpPort;
                Assert.Contains(ipPort, "9000");
                return Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientMetadata", "ToString_ReturnsFormattedString", () =>
            {
                var metadata = new ClientMetadata();
                var guid = Guid.NewGuid();
                metadata.Guid = guid;
                metadata.Ip = "127.0.0.1";
                metadata.Port = 5000;
                metadata.Name = "MyClient";

                var str = metadata.ToString();
                Assert.IsNotNull(str);
                Assert.Contains(str, guid.ToString());
                Assert.Contains(str, "127.0.0.1:5000");
                Assert.Contains(str, "MyClient");
                return Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientMetadata", "ToString_WithNullName", () =>
            {
                var metadata = new ClientMetadata();
                metadata.Ip = "127.0.0.1";
                metadata.Port = 5000;
                metadata.Name = null;

                var str = metadata.ToString();
                Assert.IsNotNull(str);
                // Should not throw with null name
                return Task.CompletedTask;
            });

            await _runner.RunTestAsync("ClientMetadata", "MultipleMetadataInstances_HaveUniqueGuids", () =>
            {
                var metadata1 = new ClientMetadata();
                var metadata2 = new ClientMetadata();
                var metadata3 = new ClientMetadata();

                Assert.AreNotEqual(metadata1.Guid, metadata2.Guid);
                Assert.AreNotEqual(metadata2.Guid, metadata3.Guid);
                Assert.AreNotEqual(metadata1.Guid, metadata3.Guid);
                return Task.CompletedTask;
            });
        }
    }
}
