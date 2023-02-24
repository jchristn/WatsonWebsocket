using GetSomeInput;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using WatsonWebsocket;

namespace Test.Server
{
    class Server
    {
        static string _ServerIp = "localhost";
        static int _ServerPort = 0;
        static bool _Ssl = false;
        static bool _AcceptInvalidCertificates = true;
        static WatsonWsServer _Server = null;
        static Guid _LastGuid = Guid.Empty;

        static void Main(string[] args)
        {
            _ServerIp = Inputty.GetString("Server IP:", "localhost", true);
            _ServerPort = Inputty.GetInteger("Server port:", 9000, true, true);
            _Ssl = Inputty.GetBoolean("Use SSL:", false);

            InitializeServer();
            // InitializeServerMultiple();
            Console.WriteLine("Please manually start the server");

            bool runForever = true;
            while (runForever)
            {
                Console.Write("Command [? for help]: ");
                string userInput = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(userInput)) continue;
                string[] splitInput = userInput.Split(new string[] { " " }, 2, StringSplitOptions.None);
                Guid guid = Guid.Empty;
                bool success = false;

                switch (splitInput[0])
                {
                    case "?":
                        Console.WriteLine("Available commands:");
                        Console.WriteLine("  ?                            help (this menu)");
                        Console.WriteLine("  q                            quit");
                        Console.WriteLine("  cls                          clear screen");
                        Console.WriteLine("  dispose                      dispose of the server");
                        Console.WriteLine("  reinit                       reinitialize the server");
                        Console.WriteLine("  start                        start accepting new connections (listening: " + _Server.IsListening + ")");
                        Console.WriteLine("  stop                         stop accepting new connections");
                        Console.WriteLine("  list                         list clients");
                        Console.WriteLine("  stats                        display server statistics");
                        Console.WriteLine("  send ip:port text message    send text to client");
                        Console.WriteLine("  send ip:port bytes message   send binary data to client");
                        Console.WriteLine("  kill ip:port                 disconnect a client");
                        break;

                    case "q":
                        runForever = false;
                        break;

                    case "cls":
                        Console.Clear();
                        break;

                    case "dispose":
                        _Server.Dispose();
                        break;

                    case "reinit":
                        InitializeServer();
                        break;

                    case "start":
                        StartServer();
                        break;

                    case "stop":
                        _Server.Stop();
                        break;

                    case "list":
                        var clients = new List<ClientMetadata>(_Server.ListClients());
                        if (clients.Count > 0)
                        {
                            Console.WriteLine("");
                            Console.WriteLine("Clients");
                            Console.WriteLine("-------");
                            foreach (ClientMetadata client in clients)
                            {
                                Console.WriteLine(client.ToString());
                            }
                            Console.WriteLine("");
                        }
                        else
                        {
                            Console.WriteLine("[No clients connected]");
                        } 
                        break;

                    case "stats":
                        Console.WriteLine(_Server.Stats.ToString());
                        break;

                    case "send":
                        if (splitInput.Length != 2) break;
                        splitInput = splitInput[1].Split(new string[] { " " }, 3, StringSplitOptions.None);
                        if (splitInput.Length != 3) break;
                        if (splitInput[0].Equals("last")) guid = _LastGuid;
                        else guid = Guid.Parse(splitInput[0]);
                        if (String.IsNullOrEmpty(splitInput[2])) break;
                        if (splitInput[1].Equals("text")) success = _Server.SendAsync(guid, splitInput[2]).Result;
                        else if (splitInput[1].Equals("bytes"))
                        {
                            byte[] data = Encoding.UTF8.GetBytes(splitInput[2]);
                            success = _Server.SendAsync(guid, data).Result;
                        }
                        else break;
                        if (!success) Console.WriteLine("Failed");
                        else Console.WriteLine("Success");
                        break;

                    case "kill":
                        if (splitInput.Length != 2) break;
                        if (splitInput[1].Equals("last")) guid = _LastGuid;
                        else guid = Guid.Parse(splitInput[1]);
                        _Server.DisconnectClient(guid);
                        break;

                    default:
                        Console.WriteLine("Unknown command: " + userInput);
                        break;
                }
            }
        }

        static void InitializeServer()
        {
            _Server = new WatsonWsServer(_ServerIp, _ServerPort, _Ssl);            
            _Server.AcceptInvalidCertificates = _AcceptInvalidCertificates;
            _Server.ClientConnected += ClientConnected;
            _Server.ClientDisconnected += ClientDisconnected;
            _Server.MessageReceived += MessageReceived;
            _Server.Logger = Console.WriteLine;
            _Server.HttpHandler = HttpHandler;
        }

        static void InitializeServerMultiple()
        {
            // original constructor
            List<string> hostnames = new List<string>
            {
                "192.168.1.163",
                "127.0.0.1"
            };

            _Server = new WatsonWsServer(hostnames, _ServerPort, _Ssl);

            // URI-based constructor
            // if (_Ssl) _Server = new WatsonWsServer(new Uri("https://" + _ServerIp + ":" + _ServerPort));
            // else _Server = new WatsonWsServer(new Uri("http://" + _ServerIp + ":" + _ServerPort));

            _Server.ClientConnected += ClientConnected;
            _Server.ClientDisconnected += ClientDisconnected;
            _Server.MessageReceived += MessageReceived;
            _Server.Logger = Console.WriteLine;
            _Server.HttpHandler = HttpHandler;
        }

        static async void StartServer()
        {                         
            // _Server.Start();
            await _Server.StartAsync();
            Console.WriteLine("Server is listening: " + _Server.IsListening);
        }

        static void ClientConnected(object sender, ConnectionEventArgs args) 
        {
            Console.WriteLine("Client " + args.Client.ToString() + " connected using URL " + args.HttpRequest.RawUrl);
            _LastGuid = args.Client.Guid;

            if (args.HttpRequest.Cookies != null && args.HttpRequest.Cookies.Count > 0)
            {
                Console.WriteLine(args.HttpRequest.Cookies.Count + " cookie(s) present:");
                foreach (Cookie cookie in args.HttpRequest.Cookies)
                {
                    Console.WriteLine("| " + cookie.Name + ": " + cookie.Value);
                }
            }
        }

        static void ClientDisconnected(object sender, DisconnectionEventArgs args)
        {
            Console.WriteLine("Client disconnected: " + args.Client.ToString());
        }

        static void MessageReceived(object sender, MessageReceivedEventArgs args)
        {
            string msg = "(null)";
            if (args.Data != null && args.Data.Count > 0) msg = Encoding.UTF8.GetString(args.Data.Array, 0, args.Data.Count);
            Console.WriteLine(args.MessageType.ToString() + " from " + args.Client.ToString() + ": " + msg);
        }

        static void HttpHandler(HttpListenerContext ctx)
        { 
            HttpListenerRequest req = ctx.Request;
            string contents = null;
            using (Stream stream = req.InputStream)
            {
                using (StreamReader readStream = new StreamReader(stream, Encoding.UTF8))
                {
                    contents = readStream.ReadToEnd();
                }
            }

            Console.WriteLine("Non-websocket request received for: " + req.HttpMethod.ToString() + " " + req.RawUrl);
            if (req.Headers != null && req.Headers.Count > 0)
            {
                Console.WriteLine("Headers:"); 
                var items = req.Headers.AllKeys.SelectMany(req.Headers.GetValues, (k, v) => new { key = k, value = v });
                foreach (var item in items)
                {
                    Console.WriteLine("  {0}: {1}", item.key, item.value);
                }
            }

            if (!String.IsNullOrEmpty(contents))
            {
                Console.WriteLine("Request body:");
                Console.WriteLine(contents);
            }
        }
    }
}