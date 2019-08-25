using System;
using System.Collections.Generic;
using System.Net;
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

        static void Main(string[] _)
        {
            string userInput;
            Console.Write("Server IP [127.0.0.1]    : ");
            userInput = Console.ReadLine()?.Trim();
            serverIp = string.IsNullOrEmpty(userInput) ? "127.0.0.1" : userInput;

            Console.Write("Server Port [8080]       : ");
            userInput = Console.ReadLine()?.Trim();
            if (!int.TryParse(userInput, out serverPort)) serverPort = 8080;

            Console.Write("SSL (true/false) [false] : ");
            userInput = Console.ReadLine()?.Trim();
            if (!bool.TryParse(userInput, out ssl)) ssl = false;

            WatsonWsServer server = new WatsonWsServer(serverIp, serverPort, ssl, true, null, ClientConnected, ClientDisconnected, MessageReceived, true);

            bool runForever = true;
            while (runForever)
            {
                Console.Write("Command [? for help]: ");
                userInput = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(userInput)) continue;
                string[] splitInput = userInput.Split(" ", 2);

                string ipPort;

                switch (splitInput[0])
                {
                    case "?":
                        Console.WriteLine("Available commands:");
                        Console.WriteLine("  ?                     help (this menu)");
                        Console.WriteLine("  q                     quit");
                        Console.WriteLine("  cls                   clear screen");
                        Console.WriteLine("  list                  list clients");
                        Console.WriteLine("  send IP:PORT MESSAGE  send message to client");
                        Console.WriteLine("  kill IP:PORT          disconnect a client");
                        break;

                    case "q":
                        runForever = false;
                        break;

                    case "cls":
                        Console.Clear();
                        break;

                    case "list":
                        var clients = new List<string>(server.ListClients());
                        if (clients.Count > 0)
                        {
                            Console.WriteLine("Clients");
                            foreach (string curr in clients)
                            {
                                Console.WriteLine("  " + curr);
                            }
                        }
                        else
                        {
                            Console.WriteLine("[No clients connected]");
                        }

                        break;

                    case "send":
                        if (splitInput.Length != 2) break;
                        splitInput = splitInput[1].Split(" ", 2);
                        if (splitInput.Length != 2) break;
                        ipPort = splitInput[0];
                        string data = splitInput[1];

                        if (string.IsNullOrEmpty(data)) break;
                        server.SendAsync(ipPort, data);
                        break;

                    case "kill":
                        if (splitInput.Length != 2) break;
                        server.KillClient(splitInput[1]);
                        break;

                    default:
                        Console.WriteLine("Unknown command: " + userInput);
                        break;
                }
            }
        }

        static Task<bool> ClientConnected(HttpListenerRequest request)
        {
            Console.WriteLine("Client connected: " + request.RemoteEndPoint);
            return Task.FromResult(true);
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