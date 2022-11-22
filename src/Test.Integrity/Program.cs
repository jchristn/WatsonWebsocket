using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WatsonWebsocket;

namespace Test.Integrity
{
    class Program
    {
        static int _NumClients = 5;
        static int _MessagesPerClient = 100;
        static int _MessageLength = 4096;
        static byte[] _MessageData = null;
        static int _SendDelayMilliseconds = 100;

        static string _Hostname = "localhost";
        static int _Port = 8000;
        static WatsonWsServer _Server = null;
        static bool _ServerReady = false;

        static readonly object _ClientsLock = new object();
        static List<Guid> _Clients = new List<Guid>();

        static Statistics _ServerStats = new Statistics();
        static readonly object _ClientStatsLock = new object();
        static List<Statistics> _ClientStats = new List<Statistics>();
         
        static void Main(string[] args)
        {
            _MessageData = Encoding.UTF8.GetBytes(RandomString(_MessageLength));
            _SendDelayMilliseconds = _NumClients * 20;

            using (_Server = new WatsonWsServer(_Hostname, _Port, false))
            {
                #region Start-Server

                _ServerStats = new Statistics();

                _Server.ClientConnected += (s, e) =>
                {
                    Console.WriteLine("Client connected: " + e.ToString());
                    lock (_ClientsLock)
                    {
                        _Clients.Add(e.Client.Guid);
                    }
                };

                _Server.ClientDisconnected += (s, e) =>
                { 
                    Console.WriteLine("*** Client disconnected: " + e.Client.Guid.ToString());
                    lock (_ClientsLock)
                    {
                        if (_Clients.Contains(e.Client.Guid)) _Clients.Remove(e.Client.Guid);
                    }
                };

                _Server.MessageReceived += (s, e) =>
                {
                    _ServerStats.AddRecv(e.Data.Count);
                };

                // server.Logger = Logger;
                _Server.Start();

                #endregion

                #region Start-and-Wait-for-Clients

                for (int i = 0; i < _NumClients; i++)
                {
                    Console.WriteLine("Starting client " + (i + 1) + "...");
                    Task.Run(() => ClientTask());
                    Task.Delay(250).Wait();
                }

                while (true)
                {
                    Task.Delay(1000).Wait();
                    int connected = 0;
                    lock (_ClientsLock)
                    {
                        connected = _Clients.Count;
                    }
                    if (connected == _NumClients) break;
                    Console.WriteLine(connected + " of " + _NumClients + " connected, waiting");
                }

                Console.WriteLine("All clients connected!");
                _ServerReady = true;

                #endregion

                #region Send-Messages-to-Clients

                for (int i = 0; i < _MessagesPerClient; i++)
                {
                    for (int j = 0; j < _NumClients; j++)
                    {
                        _Server.SendAsync(_Clients[j], _MessageData).Wait();
                        _ServerStats.AddSent(_MessageData.Length);
                    }
                }

                #endregion

                #region Wait-for-Clients

                while (true)
                {
                    Task.Delay(5000).Wait();
                    int remaining = 0;
                    lock (_ClientsLock)
                    {
                        remaining = _Clients.Count;
                        if (remaining < 1) break;
                        Console.WriteLine(DateTime.Now.ToUniversalTime().ToString("HH:mm:ss.ffffff") + " waiting for " + remaining + " clients: ");
                        foreach (Guid guid in _Clients) Console.WriteLine("| " + guid.ToString());
                    }
                }

                #endregion

                #region Statistics

                Console.WriteLine("");
                Console.WriteLine("");
                Console.WriteLine("Server statistics:");
                Console.WriteLine("  " + _ServerStats.ToString());
                Console.WriteLine("");
                Console.WriteLine("Client statistics");
                foreach (Statistics stats in _ClientStats) Console.WriteLine("  " + stats.ToString());
                Console.WriteLine("");

                #endregion
            }
        }

        static void Logger(string msg)
        {
            Console.WriteLine(msg);
        }

        static void ClientTask()
        {
            Statistics stats = new Statistics();

            using (WatsonWsClient client = new WatsonWsClient(_Hostname, _Port, false))
            {
                #region Start-Client

                client.ServerConnected += (s, e) =>
                {
                    Console.WriteLine("Client detected connection to " + _Hostname + ":" + _Port);
                };

                client.ServerDisconnected += (s, e) =>
                {
                    Console.WriteLine("Client disconnected from " + _Hostname + ":" + _Port);
                };

                client.MessageReceived += (s, e) =>
                {
                    stats.AddRecv(e.Data.Count);
                };

                // client.Logger = Logger;
                client.Start();

                #endregion

                #region Wait-for-Server-Ready

                while (!_ServerReady)
                {
                    Console.WriteLine("Client waiting for server...");
                    Task.Delay(2500).Wait();
                }

                Console.WriteLine("Client detected server ready!");

                #endregion

                #region Send-Messages-to-Server

                for (int i = 0; i < _MessagesPerClient; i++)
                {
                    Task.Delay(_SendDelayMilliseconds).Wait();
                    client.SendAsync(_MessageData).Wait();
                    stats.AddSent(_MessageData.Length);
                }

                #endregion

                #region Wait-for-Server-Messages

                while (stats.MsgRecv < _MessagesPerClient)
                {
                    Task.Delay(1000).Wait();
                }

                Console.WriteLine("Client exiting: " + stats.ToString());
                lock (_ClientStatsLock)
                {
                    _ClientStats.Add(stats);
                }

                #endregion
            }
        }

        static string RandomString(int numChar)
        {
            string ret = "";
            if (numChar < 1) return null;
            int valid = 0;
            Random random = new Random((int)DateTime.Now.Ticks);
            int num = 0;

            for (int i = 0; i < numChar; i++)
            {
                num = 0;
                valid = 0;
                while (valid == 0)
                {
                    num = random.Next(126);
                    if (((num > 47) && (num < 58)) ||
                        ((num > 64) && (num < 91)) ||
                        ((num > 96) && (num < 123)))
                    {
                        valid = 1;
                    }
                }
                ret += (char)num;
            }

            return ret;
        }
    }
}
