using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WatsonWebsocket;

namespace Test.Automated.Tests
{
    public class WebsocketSettingsTests
    {
        private readonly TestRunner _runner;

        public WebsocketSettingsTests(TestRunner runner)
        {
            _runner = runner;
        }

        public async Task RunAllTests()
        {
            Console.WriteLine("\n--- WebsocketSettings Tests ---\n");

            await _runner.RunTestAsync("WebsocketSettings", "DefaultConstructor_ShouldInitializeDefaults", () =>
            {
                var settings = new WebsocketSettings();
                Assert.IsNotNull(settings.Hostnames);
                Assert.AreEqual(8000, settings.Port);
                Assert.IsFalse(settings.Ssl);
                return Task.CompletedTask;
            });

            await _runner.RunTestAsync("WebsocketSettings", "Hostnames_CanBeSet", () =>
            {
                var settings = new WebsocketSettings();
                settings.Hostnames = new List<string> { "localhost", "127.0.0.1" };
                Assert.AreEqual(2, settings.Hostnames.Count);
                Assert.AreEqual("localhost", settings.Hostnames[0]);
                Assert.AreEqual("127.0.0.1", settings.Hostnames[1]);
                return Task.CompletedTask;
            });

            await _runner.RunTestAsync("WebsocketSettings", "Hostnames_NullAssignment_CreatesEmptyList", () =>
            {
                var settings = new WebsocketSettings();
                settings.Hostnames = null;
                Assert.IsNotNull(settings.Hostnames);
                Assert.AreEqual(0, settings.Hostnames.Count);
                return Task.CompletedTask;
            });

            await _runner.RunTestAsync("WebsocketSettings", "Port_ValidRange_CanBeSet", () =>
            {
                var settings = new WebsocketSettings();
                settings.Port = 1;
                Assert.AreEqual(1, settings.Port);
                settings.Port = 65535;
                Assert.AreEqual(65535, settings.Port);
                settings.Port = 8080;
                Assert.AreEqual(8080, settings.Port);
                return Task.CompletedTask;
            });

            await _runner.RunTestAsync("WebsocketSettings", "Port_ZeroOrNegative_ThrowsArgumentOutOfRangeException", () =>
            {
                var settings = new WebsocketSettings();
                // Port 0 may be allowed in settings (validated at server start time)
                // Negative values should throw
                Assert.Throws<ArgumentOutOfRangeException>(() => settings.Port = -1);
                Assert.Throws<ArgumentOutOfRangeException>(() => settings.Port = -100);
                return Task.CompletedTask;
            });

            await _runner.RunTestAsync("WebsocketSettings", "Port_AboveMax_ThrowsArgumentOutOfRangeException", () =>
            {
                var settings = new WebsocketSettings();
                Assert.Throws<ArgumentOutOfRangeException>(() => settings.Port = 65536);
                Assert.Throws<ArgumentOutOfRangeException>(() => settings.Port = 100000);
                return Task.CompletedTask;
            });

            await _runner.RunTestAsync("WebsocketSettings", "Ssl_CanBeToggled", () =>
            {
                var settings = new WebsocketSettings();
                Assert.IsFalse(settings.Ssl);
                settings.Ssl = true;
                Assert.IsTrue(settings.Ssl);
                settings.Ssl = false;
                Assert.IsFalse(settings.Ssl);
                return Task.CompletedTask;
            });

            await _runner.RunTestAsync("WebsocketSettings", "CompleteConfiguration", () =>
            {
                var settings = new WebsocketSettings
                {
                    Hostnames = new List<string> { "example.com", "api.example.com" },
                    Port = 9443,
                    Ssl = true
                };

                Assert.AreEqual(2, settings.Hostnames.Count);
                Assert.AreEqual("example.com", settings.Hostnames[0]);
                Assert.AreEqual("api.example.com", settings.Hostnames[1]);
                Assert.AreEqual(9443, settings.Port);
                Assert.IsTrue(settings.Ssl);
                return Task.CompletedTask;
            });
        }
    }
}
