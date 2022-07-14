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
        static int numClients = 5;
        static int messagesPerClient = 100;
        static int msgLength = 4096;
        static byte[] msgData = null;
        static int sendDelay = 100;

        static string hostname = "localhost";
        static int port = 8000;
        static WatsonWsServer server = null;
        static bool serverReady = false;

        static readonly object clientsLock = new object();
        static List<string> clients = new List<string>();

        static Statistics serverStats = new Statistics();
        static readonly object clientStatsLock = new object();
        static List<Statistics> clientStats = new List<Statistics>();
         
        static void Main(string[] args)
        {
            msgData = Encoding.UTF8.GetBytes(RandomString(msgLength));
            sendDelay = numClients * 20;

            using (server = new WatsonWsServer(hostname, port, false))
            {
                #region Start-Server

                serverStats = new Statistics();

                server.ClientConnected += (s, e) =>
                {
                    Console.WriteLine("Client connected: " + e.IpPort);
                    lock (clientsLock)
                    {
                        clients.Add(e.IpPort);
                    }
                };

                server.ClientDisconnected += (s, e) =>
                { 
                    Console.WriteLine("*** Client disconnected: " + e.IpPort);
                    lock (clientsLock)
                    {
                        if (clients.Contains(e.IpPort)) clients.Remove(e.IpPort);
                    }
                };

                server.MessageReceived += (s, e) =>
                {
                    serverStats.AddRecv(e.Data.Count);
                };

                // server.Logger = Logger;
                server.Start();

                #endregion

                #region Start-and-Wait-for-Clients

                for (int i = 0; i < numClients; i++)
                {
                    Console.WriteLine("Starting client " + (i + 1) + "...");
                    Task.Run(() => ClientTask());
                    Task.Delay(250).Wait();
                }

                while (true)
                {
                    Task.Delay(1000).Wait();
                    int connected = 0;
                    lock (clientsLock)
                    {
                        connected = clients.Count;
                    }
                    if (connected == numClients) break;
                    Console.WriteLine(connected + " of " + numClients + " connected, waiting");
                }

                Console.WriteLine("All clients connected!");
                serverReady = true;

                #endregion

                #region Send-Messages-to-Clients

                for (int i = 0; i < messagesPerClient; i++)
                {
                    for (int j = 0; j < numClients; j++)
                    {
                        server.SendAsync(clients[j], msgData).Wait();
                        serverStats.AddSent(msgData.Length);
                    }
                }

                #endregion

                #region Wait-for-Clients

                while (true)
                {
                    Task.Delay(5000).Wait();
                    int remaining = 0;
                    lock (clientsLock)
                    {
                        remaining = clients.Count;
                        if (remaining < 1) break;
                        Console.WriteLine(DateTime.Now.ToUniversalTime().ToString("HH:mm:ss.ffffff") + " waiting for " + remaining + " clients: ");
                        foreach (string curr in clients) Console.WriteLine("| " + curr);
                    }
                }

                #endregion

                #region Statistics

                Console.WriteLine("");
                Console.WriteLine("");
                Console.WriteLine("Server statistics:");
                Console.WriteLine("  " + serverStats.ToString());
                Console.WriteLine("");
                Console.WriteLine("Client statistics");
                foreach (Statistics stats in clientStats) Console.WriteLine("  " + stats.ToString());
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

            using (WatsonWsClient client = new WatsonWsClient(hostname, port, false))
            {
                #region Start-Client

                client.ServerConnected += (s, e) =>
                {
                    Console.WriteLine("Client detected connection to " + hostname + ":" + port);
                };

                client.ServerDisconnected += (s, e) =>
                {
                    Console.WriteLine("Client disconnected from " + hostname + ":" + port);
                };

                client.MessageReceived += (s, e) =>
                {
                    stats.AddRecv(e.Data.Count);
                };

                // client.Logger = Logger;
                client.Start();

                #endregion

                #region Wait-for-Server-Ready

                while (!serverReady)
                {
                    Console.WriteLine("Client waiting for server...");
                    Task.Delay(2500).Wait();
                }

                Console.WriteLine("Client detected server ready!");

                #endregion

                #region Send-Messages-to-Server

                for (int i = 0; i < messagesPerClient; i++)
                {
                    Task.Delay(sendDelay).Wait();
                    client.SendAsync(msgData).Wait();
                    stats.AddSent(msgData.Length);
                }

                #endregion

                #region Wait-for-Server-Messages

                while (stats.MsgRecv < messagesPerClient)
                {
                    Task.Delay(1000).Wait();
                }

                Console.WriteLine("Client exiting: " + stats.ToString());
                lock (clientStatsLock)
                {
                    clientStats.Add(stats);
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
    }
}
