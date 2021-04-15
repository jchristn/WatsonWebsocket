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
        static string _ServerIp = "";
        static int _ServerPort = 0;
        static bool _Ssl = false;
        static WatsonWsClient _Client = null;

        static void Main(string[] args)
        {
            _ServerIp = InputString("Server IP:", "localhost", true);
            _ServerPort = InputInteger("Server port:", 9000, true, true);
            _Ssl = InputBoolean("Use SSL:", false);

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
                        Console.WriteLine("  ?            help (this menu)");
                        Console.WriteLine("  q            quit");
                        Console.WriteLine("  cls          clear screen");
                        Console.WriteLine("  send text    send text to the server");
                        Console.WriteLine("  send bytes   send binary data to the server");
                        Console.WriteLine("  sync text    send text to the server and await response");
                        Console.WriteLine("  sync bytes   send binary data to the server and await response");
                        Console.WriteLine("  stats        display client statistics");
                        Console.WriteLine("  status       show if client connected");
                        Console.WriteLine("  dispose      dispose of the connection");
                        Console.WriteLine("  connect      connect to the server if not connected");
                        Console.WriteLine("  reconnect    disconnect if connected, then reconnect");
                        Console.WriteLine("  close        close the connection");
                        break;

                    case "q":
                        runForever = false;
                        break;

                    case "cls":
                        Console.Clear();
                        break;

                    case "send text":
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput)) break;
                        if (!_Client.SendAsync(userInput).Result) Console.WriteLine("Failed");
                        else Console.WriteLine("Success");
                        break;

                    case "send bytes":
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput)) break;
                        if (!_Client.SendAsync(Encoding.UTF8.GetBytes(userInput)).Result) Console.WriteLine("Failed");
                        break;

                    case "sync text":
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput)) break;
                        string resultStr = _Client.SendAndWaitAsync(userInput).Result;
                        if (!String.IsNullOrEmpty(resultStr))
                        {
                            Console.WriteLine("Response: " + resultStr);
                        }
                        else
                        {
                            Console.WriteLine("(null)");
                        }
                        break;

                    case "sync bytes":
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput)) break;
                        byte[] resultBytes = _Client.SendAndWaitAsync(Encoding.UTF8.GetBytes(userInput)).Result;
                        if (resultBytes != null && resultBytes.Length > 0)
                        {
                            Console.WriteLine("Response: " + Encoding.UTF8.GetString(resultBytes));
                        }
                        else
                        {
                            Console.WriteLine("(null)");
                        }
                        break;

                    case "stats":
                        Console.WriteLine(_Client.Stats.ToString());
                        break;

                    case "status":
                        if (_Client == null) Console.WriteLine("Connected: False (null)");
                        else Console.WriteLine("Connected: " + _Client.Connected);
                        break;

                    case "dispose":
                        _Client.Dispose();
                        break;

                    case "connect":
                        if (_Client != null && _Client.Connected)
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

                    case "close":
                        _Client.Stop();
                        break;

                    default:
                        break;
                }
            }
        }

        static async void InitializeClient()
        {
            if (_Client != null) _Client.Dispose();

            // original constructor
            // _Client = new WatsonWsClient(_ServerIp, _ServerPort, _Ssl);

            // URI-based constructor
            if (_Ssl) _Client = new WatsonWsClient(new Uri("wss://" + _ServerIp + ":" + _ServerPort + "/test/"));
            else _Client = new WatsonWsClient(new Uri("ws://" + _ServerIp + ":" + _ServerPort + "/test/"));

            _Client.ServerConnected += ServerConnected;
            _Client.ServerDisconnected += ServerDisconnected;
            _Client.MessageReceived += MessageReceived; 
            _Client.Logger = Logger;

            // await _Client.StartAsync();
            _Client.StartWithTimeout(10);
            Console.WriteLine("Client connected: " + _Client.Connected);
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
            string msg = "(null)";
            if (args.Data != null && args.Data.Length > 0) msg = Encoding.UTF8.GetString(args.Data);
            Console.WriteLine(args.MessageType.ToString() + " from server: " + msg);
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
