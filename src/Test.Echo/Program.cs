using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using WatsonWebsocket;

namespace Test.Echo
{
    class Program
    {  
        static string _Hostname = "localhost";
        static int _Port = 8000;
        static WatsonWsServer _Server = null;
        static Guid _ClientGuid = Guid.Empty;
        static long _ServerSendMessageCount = 5000;
        static int _ServerMessageLength = 16;
        static long _ClientSendMessageCount = 5000;
        static int _ClientMessageLength = 16;
         
        static Statistics _ServerStats = new Statistics();
        static Statistics _ClientStats = new Statistics();

        static void Main(string[] args)
        {
            string header = "[Server] ";
            using (_Server = new WatsonWsServer(_Hostname, _Port, false))
            {
                #region Start-Server
                 
                _Server.ClientConnected += (s, e) =>
                {
                    _ClientGuid = e.Client.Guid;
                    Console.WriteLine(header + "client connected: " + e.Client.ToString());
                };

                _Server.ClientDisconnected += (s, e) =>
                {
                    _ClientGuid = Guid.Empty;
                    Console.WriteLine(header + "client disconnected: " + e.Client.ToString());
                };

                _Server.MessageReceived += async (s, e) =>
                {
                    // echo it back
                    _ServerStats.AddRecv(e.Data.Count);
                    await _Server.SendAsync(e.Client.Guid, e.Data);
                    _ServerStats.AddSent(e.Data.Count);
                };

                _Server.Logger = Logger;
                _Server.Start();
                Console.WriteLine(header + "started");

                #endregion

                #region Start-Client-and-Send-Messages

                Task.Run(() => ClientTask()); 

                Task.Delay(1000).Wait();

                while (_ClientGuid == Guid.Empty) 
                {
                    Task.Delay(1000).Wait();
                    Console.WriteLine(header + "waiting for client connection");
                };

                Console.WriteLine(header + "detected client " + _ClientGuid + ", sending messages");
                 
                for (int i = 0; i < _ServerSendMessageCount; i++)
                {
                    byte[] msgData = Encoding.UTF8.GetBytes(RandomString(_ServerMessageLength));
                    _Server.SendAsync(_ClientGuid, msgData).Wait();
                    _ServerStats.AddSent(msgData.Length);
                }

                Console.WriteLine(header + "messages sent");

                #endregion

                #region Wait-for-and-Echo-Client-Messages

                while (_ClientGuid != Guid.Empty)
                {
                    Console.WriteLine(header + "waiting for client to finish");
                    Task.Delay(1000).Wait();
                }

                #endregion

                #region Statistics

                Console.WriteLine("");
                Console.WriteLine("");
                Console.WriteLine("Server statistics:");
                Console.WriteLine("  " + _ServerStats.ToString());
                Console.WriteLine("");
                Console.WriteLine("Client statistics");
                Console.WriteLine("  " + _ClientStats.ToString());
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

            using (WatsonWsClient client = new WatsonWsClient(_Hostname, _Port, false))
            {
                #region Start-Client

                client.ServerConnected += (s, e) =>
                { 
                    Console.WriteLine(header + "connected to " + _Hostname + ":" + _Port);
                };

                client.ServerDisconnected += (s, e) =>
                {
                    Console.WriteLine(header + "disconnected from " + _Hostname + ":" + _Port);
                };

                client.MessageReceived += (s, e) =>
                {
                    _ClientStats.AddRecv(e.Data.Count);
                };

                client.Logger = Logger;
                client.Start();
                Console.WriteLine(header + "started");

                #endregion

                #region Wait-for-Messages

                while (_ClientStats.MsgRecv < _ServerSendMessageCount) 
                {
                    Task.Delay(1000).Wait();
                    Console.WriteLine(header + "waiting for server messages");
                };

                Console.WriteLine(header + "server messages received");
                #endregion

                #region Send-Messages

                Console.WriteLine(header + "sending messages to server");

                for (int i = 0; i < _ClientSendMessageCount; i++)
                {
                    byte[] msgData = Encoding.UTF8.GetBytes(RandomString(_ClientMessageLength));
                    await client.SendAsync(msgData);
                    _ClientStats.AddSent(msgData.Length);
                }

                while (_ClientStats.MsgRecv < (_ClientSendMessageCount + _ServerSendMessageCount))
                {
                    Console.WriteLine(header + "waiting for server echo messages");
                    Task.Delay(1000).Wait();
                }

                Console.WriteLine(header + "finished");
                _ClientGuid = Guid.Empty;

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
