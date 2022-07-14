using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using WatsonWebsocket;

namespace Test.Echo
{
    class Program
    {  
        static string hostname = "localhost";
        static int port = 8000;
        static WatsonWsServer server = null;
        static string clientIpPort = null;
        static long serverSendMessageCount = 5000;
        static int serverMessageLength = 16;
        static long clientSendMessageCount = 5000;
        static int clientMessageLength = 16;
         
        static Statistics serverStats = new Statistics();
        static Statistics clientStats = new Statistics();

        static void Main(string[] args)
        {
            string header = "[Server] ";
            using (server = new WatsonWsServer(hostname, port, false))
            {
                #region Start-Server
                 
                server.ClientConnected += (s, e) =>
                {
                    clientIpPort = e.IpPort;
                    Console.WriteLine(header + "client connected: " + e.IpPort);
                };

                server.ClientDisconnected += (s, e) =>
                {
                    clientIpPort = null;
                    Console.WriteLine(header + "client disconnected: " + e.IpPort);
                };

                server.MessageReceived += async (s, e) =>
                {
                    // echo it back
                    serverStats.AddRecv(e.Data.Count);
                    await server.SendAsync(e.IpPort, e.Data);
                    serverStats.AddSent(e.Data.Count);
                };

                server.Logger = Logger;
                server.Start();
                Console.WriteLine(header + "started");

                #endregion

                #region Start-Client-and-Send-Messages

                Task.Run(() => ClientTask()); 

                Task.Delay(1000).Wait();

                while (String.IsNullOrEmpty(clientIpPort)) 
                {
                    Task.Delay(1000).Wait();
                    Console.WriteLine(header + "waiting for client connection");
                };

                Console.WriteLine(header + "detected client " + clientIpPort + ", sending messages");
                 
                for (int i = 0; i < serverSendMessageCount; i++)
                {
                    byte[] msgData = Encoding.UTF8.GetBytes(RandomString(serverMessageLength));
                    server.SendAsync(clientIpPort, msgData).Wait();
                    serverStats.AddSent(msgData.Length);
                }

                Console.WriteLine(header + "messages sent");

                #endregion

                #region Wait-for-and-Echo-Client-Messages

                while (!String.IsNullOrEmpty(clientIpPort))
                {
                    Console.WriteLine(header + "waiting for client to finish");
                    Task.Delay(1000).Wait();
                }

                #endregion

                #region Statistics

                Console.WriteLine("");
                Console.WriteLine("");
                Console.WriteLine("Server statistics:");
                Console.WriteLine("  " + serverStats.ToString());
                Console.WriteLine("");
                Console.WriteLine("Client statistics");
                Console.WriteLine("  " + clientStats.ToString());
                Console.WriteLine("");

                #endregion

                Console.WriteLine("Press ENTER to exit");
                Console.ReadLine();
            }
        }

        static void Logger(string msg)
        {
            Console.WriteLine(msg);
        }

        static async void ClientTask()
        {
            string header = "[Client] "; 

            using (WatsonWsClient client = new WatsonWsClient(hostname, port, false))
            {
                #region Start-Client

                client.ServerConnected += (s, e) =>
                { 
                    Console.WriteLine(header + "connected to " + hostname + ":" + port);
                };

                client.ServerDisconnected += (s, e) =>
                {
                    Console.WriteLine(header + "disconnected from " + hostname + ":" + port);
                };

                client.MessageReceived += (s, e) =>
                {
                    clientStats.AddRecv(e.Data.Count);
                };

                client.Logger = Logger;
                client.Start();
                Console.WriteLine(header + "started");

                #endregion

                #region Wait-for-Messages

                while (clientStats.MsgRecv < serverSendMessageCount) 
                {
                    Task.Delay(1000).Wait();
                    Console.WriteLine(header + "waiting for server messages");
                };

                Console.WriteLine(header + "server messages received");
                #endregion

                #region Send-Messages

                Console.WriteLine(header + "sending messages to server");

                for (int i = 0; i < clientSendMessageCount; i++)
                {
                    byte[] msgData = Encoding.UTF8.GetBytes(RandomString(clientMessageLength));
                    await client.SendAsync(msgData);
                    clientStats.AddSent(msgData.Length);
                }

                while (clientStats.MsgRecv < (clientSendMessageCount + serverSendMessageCount))
                {
                    Console.WriteLine(header + "waiting for server echo messages");
                    Task.Delay(1000).Wait();
                }

                Console.WriteLine(header + "finished");
                clientIpPort = null;

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
