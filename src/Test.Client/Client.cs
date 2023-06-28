using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using GetSomeInput;
using WatsonWebsocket;

namespace Test.Client
{
    class Client
    {
        static string _ServerIp = "";
        static int _ServerPort = 0;
        static bool _Ssl = false;
        static bool _AcceptInvalidCertificates = true;
        static WatsonWsClient _Client = null;
        static Guid _Guid = Guid.Parse("12345678-1234-1234-1234-123456789012");

        static void Main(string[] args)
        {
            _ServerIp = Inputty.GetString("Server IP:", "localhost", true);
            _ServerPort = Inputty.GetInteger("Server port:", 9000, true, true);
            _Ssl = Inputty.GetBoolean("Use SSL:", false);

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
                        Console.WriteLine("  cookie       add a cookie");
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
                        var resultBytes = _Client.SendAndWaitAsync(Encoding.UTF8.GetBytes(userInput)).Result;
                        if (resultBytes != null && resultBytes.Count > 0)
                        {
                            Console.WriteLine("Response: " + Encoding.UTF8.GetString(resultBytes.Array, 0, resultBytes.Count));
                        }
                        else
                        {
                            Console.WriteLine("(null)");
                        }
                        break;

                    case "cookie":
                        Console.Write("Key    : ");
                        string key = Console.ReadLine();
                        if (!String.IsNullOrEmpty(key))
                        {
                            Console.Write("Value  : ");
                            string val = Console.ReadLine();
                            Console.Write("Path   : ");
                            string path = Console.ReadLine();
                            Console.Write("Domain : ");
                            string domain = Console.ReadLine();
                            _Client.AddCookie(new Cookie(key, val, path, domain));
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

        static void InitializeClient()
        {
            if (_Client != null) _Client.Dispose();

            // original constructor
            // _Client = new WatsonWsClient(_ServerIp, _ServerPort, _Ssl);

            // URI-based constructor
            if (_Ssl) _Client = new WatsonWsClient(new Uri("wss://" + _ServerIp + ":" + _ServerPort));
            else _Client = new WatsonWsClient(new Uri("ws://" + _ServerIp + ":" + _ServerPort));

            _Client.AcceptInvalidCertificates = _AcceptInvalidCertificates;
            _Client.ServerConnected += ServerConnected;
            _Client.ServerDisconnected += ServerDisconnected;
            _Client.MessageReceived += MessageReceived; 
            _Client.Logger = Logger;
            _Client.AddCookie(new System.Net.Cookie("foo", "bar", "/", "localhost"));

            // await _Client.StartAsync();
            _Client.StartWithTimeoutAsync(30).Wait();
            Console.WriteLine("Client connected: " + _Client.Connected);
        }

        static void Logger(string msg)
        {
            Console.WriteLine(msg);
        }

        static void MessageReceived(object sender, MessageReceivedEventArgs args)
        {
            string msg = "(null)";
            if (args.Data != null && args.Data.Count > 0) msg = Encoding.UTF8.GetString(args.Data.Array, 0, args.Data.Count);
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
