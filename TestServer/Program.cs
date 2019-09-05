using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using WatsonWebsocket;

namespace TestServer
{
    class Program
    {
        static string serverIp = "";
        static int serverPort = 0;
        static bool ssl = false;
        static WatsonWsServer server = null;

        static void Main(string[] args)
        {
            serverIp = InputString("Server IP:", "127.0.0.1", true);
            serverPort = InputInteger("Server port:", 9000, true, true);
            ssl = InputBoolean("Use SSL:", false);

            InitializeServer();

            bool runForever = true;
            while (runForever)
            {
                Console.Write("Command [? for help]: ");
                string userInput = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(userInput)) continue;
                string[] splitInput = userInput.Split(new string[] { " " }, 2, StringSplitOptions.None);
                string ipPort = null;
                bool success = false;

                switch (splitInput[0])
                {
                    case "?":
                        Console.WriteLine("Available commands:");
                        Console.WriteLine("  ?                     help (this menu)");
                        Console.WriteLine("  q                     quit");
                        Console.WriteLine("  cls                   clear screen");
                        Console.WriteLine("  list                  list clients");
                        Console.WriteLine("  send ip:port message  send message to client");
                        Console.WriteLine("  kill ip:port          disconnect a client");
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
                        splitInput = splitInput[1].Split(new string[] { " " }, 2, StringSplitOptions.None);
                        if (splitInput.Length != 2) break;
                        ipPort = splitInput[0];
                        string data = splitInput[1];

                        if (string.IsNullOrEmpty(data)) break;
                        success = server.SendAsync(ipPort, data).Result;
                        break;

                    case "kill":
                        if (splitInput.Length != 2) break;
                        server.DisconnectClient(splitInput[1]);
                        break;

                    default:
                        Console.WriteLine("Unknown command: " + userInput);
                        break;
                }
            }
        }

        static void InitializeServer()
        {
            server = new WatsonWsServer(
                serverIp,
                serverPort,
                ssl);

            server.ClientConnected = ClientConnected;
            server.ClientDisconnected = ClientDisconnected;
            server.MessageReceived = MessageReceived; 

            server.Start();
        }

        static bool InputBoolean(string question, bool yesDefault)
        {
            Console.Write(question);

            if (yesDefault) Console.Write(" [Y/n]? ");
            else Console.Write(" [y/N]? ");

            string userInput = Console.ReadLine();

            if (String.IsNullOrEmpty(userInput))
            {
                if (yesDefault) return true;
                return false;
            }

            userInput = userInput.ToLower();

            if (yesDefault)
            {
                if (
                    (String.Compare(userInput, "n") == 0)
                    || (String.Compare(userInput, "no") == 0)
                   )
                {
                    return false;
                }

                return true;
            }
            else
            {
                if (
                    (String.Compare(userInput, "y") == 0)
                    || (String.Compare(userInput, "yes") == 0)
                   )
                {
                    return true;
                }

                return false;
            }
        }

        static string InputString(string question, string defaultAnswer, bool allowNull)
        {
            while (true)
            {
                Console.Write(question);

                if (!String.IsNullOrEmpty(defaultAnswer))
                {
                    Console.Write(" [" + defaultAnswer + "]");
                }

                Console.Write(" ");

                string userInput = Console.ReadLine();

                if (String.IsNullOrEmpty(userInput))
                {
                    if (!String.IsNullOrEmpty(defaultAnswer)) return defaultAnswer;
                    if (allowNull) return null;
                    else continue;
                }

                return userInput;
            }
        }

        static int InputInteger(string question, int defaultAnswer, bool positiveOnly, bool allowZero)
        {
            while (true)
            {
                Console.Write(question);
                Console.Write(" [" + defaultAnswer + "] ");

                string userInput = Console.ReadLine();

                if (String.IsNullOrEmpty(userInput))
                {
                    return defaultAnswer;
                }

                int ret = 0;
                if (!Int32.TryParse(userInput, out ret))
                {
                    Console.WriteLine("Please enter a valid integer.");
                    continue;
                }

                if (ret == 0)
                {
                    if (allowZero)
                    {
                        return 0;
                    }
                }

                if (ret < 0)
                {
                    if (positiveOnly)
                    {
                        Console.WriteLine("Please enter a value greater than zero.");
                        continue;
                    }
                }

                return ret;
            }
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        static async Task<bool> ClientConnected(string ipPort, HttpListenerRequest request)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            Console.WriteLine("Client connected: " + ipPort);
            return true;
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        static async Task ClientDisconnected(string ipPort)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            Console.WriteLine("Client disconnected: " + ipPort);
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        static async Task MessageReceived(string ipPort, byte[] data)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            string msg = "";
            if (data != null && data.Length > 0) msg = Encoding.UTF8.GetString(data);
            Console.WriteLine("Message received from " + ipPort + ": " + msg);
        }
    }
}