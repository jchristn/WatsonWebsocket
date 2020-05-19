using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WatsonWebsocket;

namespace Test.Client
{
    class Program
    {
        static string serverIp = "";
        static int serverPort = 0;
        static bool ssl = false;
        static WatsonWsClient client = null;

        static void Main(string[] args)
        {
            serverIp = InputString("Server IP:", "localhost", true);
            serverPort = InputInteger("Server port:", 9000, true, true);
            ssl = InputBoolean("Use SSL:", false);

            InitializeClient();

            bool runForever = true;
            while (runForever)
            {
                Console.Write("Command [? for help]: ");
                string userInput = Console.ReadLine();
                if (String.IsNullOrEmpty(userInput)) continue;

                switch (userInput)
                {
                    case "?":
                        Console.WriteLine("Available commands:");
                        Console.WriteLine("  ?          help (this menu)");
                        Console.WriteLine("  q          quit");
                        Console.WriteLine("  cls        clear screen");
                        Console.WriteLine("  send       send message to server");
                        Console.WriteLine("  stats      display client statistics");
                        Console.WriteLine("  status     show if client connected");
                        Console.WriteLine("  dispose    dispose of the connection");
                        Console.WriteLine("  connect    connect to the server if not connected");
                        Console.WriteLine("  reconnect  disconnect if connected, then reconnect");
                        break;

                    case "q":
                        runForever = false;
                        break;

                    case "cls":
                        Console.Clear();
                        break;

                    case "send":
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput)) break;
                        if (!client.SendAsync(Encoding.UTF8.GetBytes(userInput)).Result) Console.WriteLine("Failed");
                        break;

                    case "stats":
                        Console.WriteLine(client.Stats.ToString());
                        break;

                    case "status":
                        if (client == null) Console.WriteLine("Connected: False (null)");
                        else Console.WriteLine("Connected: " + client.Connected);
                        break;

                    case "dispose":
                        client.Dispose();
                        break;

                    case "connect":
                        if (client != null && client.Connected)
                        {
                            Console.WriteLine("Already connected");
                        }
                        else
                        {
                            InitializeClient();
                        }
                        break;

                    case "reconnect":
                        InitializeClient();
                        break;

                    default:
                        break;
                }
            }
        }

        static void InitializeClient()
        {
            if (client != null) client.Dispose();

            client = new WatsonWsClient(
                serverIp,
                serverPort,
                ssl);

            client.ServerConnected += ServerConnected;
            client.ServerDisconnected += ServerDisconnected;
            client.MessageReceived += MessageReceived;

            client.Logger = Logger;
            client.Start(); 
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
         
        static void Logger(string msg)
        {
            Console.WriteLine(msg);
        }

        static void MessageReceived(object sender, MessageReceivedEventArgs args) 
        {
            Console.WriteLine("Message from server: " + Encoding.UTF8.GetString(args.Data));
        }
         
        static void ServerConnected(object sender, EventArgs args)
        {
            Console.WriteLine("Server connected");
        }

        static void ServerDisconnected(object sender, EventArgs args)
        {
            Console.WriteLine("Server disconnected");
        }
    }
}
