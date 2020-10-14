using System;
using System.Text;
using System.Threading.Tasks;
using WatsonWebsocket;

namespace Test.Dispose
{
    class Program
    {
        static WatsonWsServer _Server = null;

        static void Main(string[] args)
        {
            // test1
            _Server = new WatsonWsServer("localhost", 9000, false);
            _Server.ClientConnected += (s, a) => { /* Console.WriteLine("Client " + a.IpPort + " connected"); */ };
            _Server.ClientDisconnected += (s, a) => { /* Console.WriteLine("Client " + a.IpPort + " disconnected"); */ };
            _Server.MessageReceived += (s, a) => { /* Console.WriteLine(Encoding.UTF8.GetString(a.Data)); */ }; 
            _Server.Start();
            Console.WriteLine("Test 1 with server started: " + ClientTask());

            // test2
            Task.Delay(1000).Wait();
            _Server.Stop();
            Console.WriteLine("Test 2 with server stopped: " + ClientTask());

            // test3
            Task.Delay(1000).Wait();
            _Server.Start();
            Console.WriteLine("Test 3 with server restarted: " + ClientTask());

            // test4
            Task.Delay(1000).Wait();
            _Server.Dispose();
            Console.WriteLine("Test 4 with server disposed: " + ClientTask());

            // test5
            Task.Delay(1000).Wait();
            _Server = new WatsonWsServer("localhost", 9000, false);
            _Server.ClientConnected += (s, a) => { /* Console.WriteLine("Client " + a.IpPort + " connected"); */ };
            _Server.ClientDisconnected += (s, a) => { /* Console.WriteLine("Client " + a.IpPort + " disconnected"); */ };
            _Server.MessageReceived += (s, a) => { /* Console.WriteLine(Encoding.UTF8.GetString(a.Data)); */ }; 
            _Server.Start();
            Console.WriteLine("Test 5 with server started: " + ClientTask()); 
        }

        static bool ClientTask()
        { 
            try
            {
                WatsonWsClient client = new WatsonWsClient("localhost", 9000, false);
                client.Start();
                Task.Delay(1000).Wait();
                return client.SendAsync("Hello").Result;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                return false;
            }
        }
    }
}
