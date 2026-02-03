using System;
using System.Threading.Tasks;
using Test.Automated.Tests;

namespace Test.Automated
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.WriteLine();
            Console.WriteLine("================================================================================");
            Console.WriteLine("  WatsonWebsocket Automated Test Suite");
            Console.WriteLine("================================================================================");
            Console.WriteLine();
            Console.WriteLine($"Start Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Runtime:    {Environment.Version}");
            Console.WriteLine($"OS:         {Environment.OSVersion}");
            Console.WriteLine($"Machine:    {Environment.MachineName}");
            Console.WriteLine();

            var runner = new TestRunner();
            runner.StartOverallTimer();

            try
            {
                // Unit Tests - Testing individual classes in isolation
                Console.WriteLine("================================================================================");
                Console.WriteLine("  UNIT TESTS");
                Console.WriteLine("================================================================================");

                var websocketSettingsTests = new WebsocketSettingsTests(runner);
                await websocketSettingsTests.RunAllTests();

                var statisticsTests = new StatisticsTests(runner);
                await statisticsTests.RunAllTests();

                var clientMetadataTests = new ClientMetadataTests(runner);
                await clientMetadataTests.RunAllTests();

                // Component Tests - Testing WatsonWsServer and WatsonWsClient
                Console.WriteLine();
                Console.WriteLine("================================================================================");
                Console.WriteLine("  COMPONENT TESTS");
                Console.WriteLine("================================================================================");

                var serverTests = new ServerTests(runner);
                await serverTests.RunAllTests();

                var clientTests = new ClientTests(runner);
                await clientTests.RunAllTests();

                // Integration Tests - Testing client-server interaction
                Console.WriteLine();
                Console.WriteLine("================================================================================");
                Console.WriteLine("  INTEGRATION TESTS");
                Console.WriteLine("================================================================================");

                var integrationTests = new IntegrationTests(runner);
                await integrationTests.RunAllTests();
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("FATAL ERROR during test execution:");
                Console.WriteLine(ex.ToString());
            }
            finally
            {
                runner.StopOverallTimer();
            }

            Console.WriteLine();
            runner.PrintSummary();

            Console.WriteLine();
            Console.WriteLine($"End Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine();

            return runner.GetExitCode();
        }
    }
}
