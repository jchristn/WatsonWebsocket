using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WatsonWebsocket;

namespace TestServerNetCore
{
    class TestServer
    {
        static string serverIp = "";
        static int serverPort = 0;
        static bool ssl = false;

        static void Main(string[] args)
        {
            Console.Write("Server IP        : ");
            serverIp = Console.ReadLine();

            Console.Write("Server Port      : ");
            serverPort = Convert.ToInt32(Console.ReadLine());

            Console.Write("SSL (true/false) : ");
            ssl = Convert.ToBoolean(Console.ReadLine());

            WatsonWsServer server = new WatsonWsServer(serverIp, serverPort, ssl, true, null, ClientConnected, ClientDisconnected, MessageReceived, true);

            bool runForever = true;
            while (runForever)
            {
                Console.Write("Command [? for help]: ");
                string userInput = Console.ReadLine();

                List<string> clients;
                string ipPort;

                if (String.IsNullOrEmpty(userInput)) continue;

                switch (userInput)
                {
                    case "?":
                        Console.WriteLine("Available commands:");
                        Console.WriteLine("  ?       help (this menu)");
                        Console.WriteLine("  q       quit");
                        Console.WriteLine("  cls     clear screen");
                        Console.WriteLine("  list    list clients");
                        Console.WriteLine("  send    send message to client");
                        Console.WriteLine("  kill    disconnect a client");
                        break;

                    case "q":
                        runForever = false;
                        break;

                    case "cls":
                        Console.Clear();
                        break;

                    case "list":
                        clients = new List<string>(server.ListClients());
                        if (clients != null && clients.Count > 0)
                        {
                            Console.WriteLine("Clients");
                            foreach (string curr in clients)
                            {
                                Console.WriteLine("  " + curr);
                            }
                        }
                        else
                        {
                            Console.WriteLine("None");
                        }
                        break;

                    case "send":
                        Console.Write("IP:Port: ");
                        ipPort = Console.ReadLine();
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput)) break;
                        server.SendAsync(ipPort, Encoding.UTF8.GetBytes(userInput));
                        break;

                    case "kill":
                        Console.Write("IP:Port: ");
                        ipPort = Console.ReadLine();
                        server.KillClient(ipPort);
                        break;

                    default:
                        break;
                }
            }
        }

        static bool ClientConnected(string ipPort, IDictionary<string, string> queryString)
        {
            Console.WriteLine("Client connected: " + ipPort);
            if (queryString != null && queryString.Count > 0)
            {
                Console.WriteLine("Querystring: ");
                foreach (KeyValuePair<string, string> kvp in queryString)
                {
                    Console.WriteLine("- " + kvp.Key + ": " + kvp.Value);
                }
            }
            return true;
        }

        static bool ClientDisconnected(string ipPort)
        {
            Console.WriteLine("Client disconnected: " + ipPort);
            return true;
        }

        static bool MessageReceived(string ipPort, byte[] data)
        {
            string msg = "";
            if (data != null && data.Length > 0) msg = Encoding.UTF8.GetString(data);
            Console.WriteLine("Message received from " + ipPort + ": " + msg);
            return true;
        }
    }
}
