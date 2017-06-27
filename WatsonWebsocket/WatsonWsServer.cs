using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.Net.IPAddress;

namespace WatsonWebsocket
{    
    /// <summary>
    /// Watson TCP server.
    /// </summary>
    public class WatsonWsServer : IDisposable
    {
        #region Public-Members

        #endregion

        #region Private-Members

        private readonly bool Debug;
        private readonly string ListenerIp;
        private readonly int ListenerPort;
        private readonly IPAddress ListenerIpAddress;
        private readonly string ListenerPrefix;
        private readonly HttpListener Listener;
        private int _activeClients;
        private readonly ConcurrentDictionary<string, ClientMetadata> Clients;
        private readonly List<string> PermittedIps;
        private readonly CancellationTokenSource TokenSource;
        private CancellationToken _token;
        private readonly Func<string, IDictionary<string, string>, bool> ClientConnected;
        private readonly Func<string, bool> ClientDisconnected;
        private readonly Func<string, byte[], bool> MessageReceived;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes the Watson websocket server.
        /// </summary>
        public WatsonWsServer(
            string listenerIp,
            int listenerPort,
            bool ssl,
            bool acceptInvalidCerts,
            IEnumerable<string> permittedIps,
            Func<string, IDictionary<string, string>, bool> clientConnected,
            Func<string, bool> clientDisconnected,
            Func<string, byte[], bool> messageReceived,
            bool debug)
        {
            if (listenerPort < 1) throw new ArgumentOutOfRangeException(nameof(listenerPort));

            ClientConnected = clientConnected ?? null;

            ClientDisconnected = clientDisconnected ?? null;

            PermittedIps = null;

            MessageReceived = messageReceived ?? throw new ArgumentNullException(nameof(MessageReceived));
            Debug = debug;

            if (permittedIps != null && permittedIps.Any()) PermittedIps = new List<string>(permittedIps);

            if (String.IsNullOrEmpty(listenerIp))
            {
                ListenerIpAddress = Loopback;
                ListenerIp = ListenerIpAddress.ToString();
            }
            else if (listenerIp == "*")
            {
                ListenerIp = "*";
                ListenerIpAddress = Any;
            }
            else
            {
                ListenerIpAddress = Parse(listenerIp);
                ListenerIp = listenerIp;
            }

            ListenerPort = listenerPort;

            if (ssl) ListenerPrefix = "https://" + ListenerIp + ":" + ListenerPort + "/";
            else ListenerPrefix = "http://" + ListenerIp + ":" + ListenerPort + "/";

            if (acceptInvalidCerts) ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;

            Listener = new HttpListener();
            Listener.Prefixes.Add(ListenerPrefix);
            Log("WatsonWsServer starting on " + ListenerPrefix);

            TokenSource = new CancellationTokenSource();
            _token = TokenSource.Token;
            _activeClients = 0;
            Clients = new ConcurrentDictionary<string, ClientMetadata>();
            Task.Run(AcceptConnections, _token);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Tear down the server and dispose of background workers.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Send data to the specified client, asynchronously.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string ipPort, byte[] data)
        {
            ClientMetadata client;
            if (!Clients.TryGetValue(ipPort, out client))
            {
                Log("Send unable to find client " + ipPort);
                return false;
            }

            return await MessageWriteAsync(client, data);
        }

        /// <summary>
        /// Determine whether or not the specified client is connected to the server.
        /// </summary>
        /// <returns>Boolean indicating if the client is connected to the server.</returns>
        public bool IsClientConnected(string ipPort)
        {
            ClientMetadata client;
            return (Clients.TryGetValue(ipPort, out client));
        }

        /// <summary>
        /// List the IP:port of each connected client.
        /// </summary>
        /// <returns>A string list containing each client IP:port.</returns>
        public List<string> ListClients()
        {
            Dictionary<string, ClientMetadata> clients = Clients.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            List<string> ret = new List<string>();
            foreach (KeyValuePair<string, ClientMetadata> curr in clients)
            {
                ret.Add(curr.Key);
            }
            return ret;
        }

        #endregion

        #region Private-Methods

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Listener?.Stop();
                TokenSource.Cancel();
            }
        }

        private void Log(string msg)
        {
            if (Debug)
            {
                Console.WriteLine(msg);
            }
        }

        private void LogException(string method, Exception e)
        {
            Log("================================================================================");
            Log(" = Method: " + method);
            Log(" = Exception Type: " + e.GetType().ToString());
            Log(" = Exception Data: " + e.Data);
            Log(" = Inner Exception: " + e.InnerException);
            Log(" = Exception Message: " + e.Message);
            Log(" = Exception Source: " + e.Source);
            Log(" = Exception StackTrace: " + e.StackTrace);
            Log("================================================================================");
        }

        private string BytesToHex(byte[] data)
        {
            if (data == null || data.Length < 1) return "(null)";
            return BitConverter.ToString(data).Replace("-", "");
        }

        private async Task AcceptConnections()
        {
            try
            {
                #region Accept-WS-Connections

                Listener.Start();
                
                while (!_token.IsCancellationRequested)
                { 
                    #region Accept-Connection

                    HttpListenerContext httpContext = await Listener.GetContextAsync();

                    #endregion

                    #region Get-Tuple-and-Check-IP

                    if (httpContext.Request.RemoteEndPoint != null)
                    {
                        string clientIp = httpContext.Request.RemoteEndPoint.Address.ToString();
                        int clientPort = httpContext.Request.RemoteEndPoint.Port;

                        var query = new Dictionary<string, string>();
                        var requestQuery = httpContext.Request.QueryString;
                        foreach (var key in requestQuery.AllKeys)
                        {
                            query[key] = requestQuery[key];
                        }

                        if (PermittedIps != null && PermittedIps.Count > 0)
                        {
                            if (!PermittedIps.Contains(clientIp))
                            {
                                Log("*** AcceptConnections rejecting connection from " + clientIp + " (not permitted)");
                                httpContext.Response.StatusCode = 401;
                                httpContext.Response.Close();
                                return;
                            }
                        }

                        Log("AcceptConnections accepted connection from " + clientIp + ":" + clientPort);

                        #endregion

                        #region Get-Websocket-Context

                        WebSocketContext wsContext = null;
                        try
                        {
                            wsContext = httpContext.AcceptWebSocketAsync(subProtocol: null).Result;
                        }
                        catch (Exception)
                        {
                            Log("*** AcceptConnections unable to retrieve websocket content for client " + clientIp + ":" + clientPort);
                            httpContext.Response.StatusCode = 500;
                            httpContext.Response.Close();
                            return;
                        }

                        WebSocket ws = wsContext.WebSocket;

                        #endregion

                        var unawaited = Task.Run(() =>
                        { 
                            #region Add-to-Client-List

                            _activeClients++;
                            // Do not decrement in this block, decrement is done by the connection reader

                            ClientMetadata currClient = new ClientMetadata(httpContext, ws, wsContext);
                            if (!AddClient(currClient))
                            {
                                Log("*** AcceptConnections unable to add client " + clientIp + ":" + clientPort);
                                httpContext.Response.StatusCode = 500;
                                httpContext.Response.Close();
                                return;
                            }

                            #endregion

                            #region Start-Data-Receiver

                            CancellationToken dataReceiverToken = default(CancellationToken);

                            Log("AcceptConnections starting data receiver for " + clientIp + ":" + clientPort + " (now " + _activeClients + " clients)");
                            if (ClientConnected != null)
                            {
                                Task.Run(() => ClientConnected(clientIp + ":" + clientPort, query), dataReceiverToken);
                            }

                            Task.Run(async () => await DataReceiver(currClient, dataReceiverToken), dataReceiverToken);

                            #endregion 

                        }, _token);
                    }
                }

                #endregion
            }
            catch (Exception e)
            {
                LogException("AcceptConnections", e);
            }
        }
        
        private async Task DataReceiver(ClientMetadata client, CancellationToken? cancelToken = null)
        { 
            try
            {
                #region Wait-for-Data

                while (true)
                {
                    cancelToken?.ThrowIfCancellationRequested();
                    
                    byte[] data = await MessageReadAsync(client);
                    if (data == null)
                    {
                        // no message available
                        await Task.Delay(30, _token);
                        continue;
                    }
                    else
                    {
                        if (data.Length == 1 && data[0] == 0x00)
                        {
                            break;
                        }
                    }

                    if (MessageReceived != null)
                    {
                        var unawaited = Task.Run(() => MessageReceived(client.IpPort(), data), _token);
                    }
                }

                #endregion
            }
            catch (OperationCanceledException oce)
            {
                Log("DataReceiver client " + client.IpPort() + " disconnected (canceled): " + oce.Message);
                throw; //normal cancellation
            }
            catch (WebSocketException wse)
            {
                Log("DataReceiver client " + client.IpPort() + " disconnected (websocket exception): " + wse.Message);
            }
            finally
            {
                _activeClients--;
                RemoveClient(client);
                if (ClientDisconnected != null)
                {
                    var unawaited = Task.Run(() => ClientDisconnected(client.IpPort()), _token);
                }
                Log("DataReceiver client " + client.IpPort() + " disconnected (now " + _activeClients + " clients active)");
            }
        }

        private bool AddClient(ClientMetadata client)
        { 
            ClientMetadata removedClient;
            if (!Clients.TryRemove(client.IpPort(), out removedClient))
            {
                // do nothing, it probably did not exist anyway
            }

            Clients.TryAdd(client.IpPort(), client);
            Log("AddClient added client " + client.IpPort());
            return true;
        }

        private bool RemoveClient(ClientMetadata client)
        { 
            ClientMetadata removedClient;
            if (!Clients.TryRemove(client.IpPort(), out removedClient))
            {
                Log("RemoveClient unable to remove client " + client.IpPort());
                return false;
            }
            else
            {
                Log("RemoveClient removed client " + client.IpPort());
                return true;
            }
        }

        private async Task<byte[]> MessageReadAsync(ClientMetadata client)
        {
            /*
             *
             * Do not catch exceptions, let them get caught by the data reader
             * to destroy the connection
             *
             */

            if (client.HttpContext == null) return null;
            if (client.WsContext == null) return null;

            #region Variables

            byte[] contentBytes;

            #endregion

            #region Read-Data

            using (MemoryStream dataMs = new MemoryStream())
            {
                const long bufferSize = 16*1024;

                var buffer = new byte[bufferSize];
                var bufferSegment = new ArraySegment<byte>(buffer);
                while (client.Ws.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult receiveResult = await client.Ws.ReceiveAsync(bufferSegment, CancellationToken.None);
                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        //
                        // end of message
                        //
                        break;
                    }
                    else
                    {
                        dataMs.Write(buffer, 0, receiveResult.Count);

                        // check if read fully
                        if (receiveResult.EndOfMessage) break;
                    }
                }

                contentBytes = dataMs.ToArray();
            }

            #endregion

            #region Check-Content-Bytes

            if (contentBytes.Length < 1)
            {
                Log("*** MessageReadAsync " + client.IpPort() + " no content read");
                return null;
            }

            #endregion

            return contentBytes;
        }

        private async Task<bool> MessageWriteAsync(ClientMetadata client, byte[] data)
        { 
            try
            {
                #region Send-Message

                // can't have two simultaneous SendAsync calls so use a semaphore to block the second until the first has completed
                await client.SendAsyncLock.WaitAsync(_token);
                if (_token.IsCancellationRequested)
                {
                    return false;
                }
                try
                {
                    await client.Ws.SendAsync(new ArraySegment<byte>(data, 0, data.Length),
                        WebSocketMessageType.Binary, true, CancellationToken.None);
                }
                finally
                {
                    client.SendAsyncLock.Release();
                }
                return true;

                #endregion
            }
            catch (Exception)
            {
                Log("*** MessageWriteAsync " + client.IpPort() + " disconnected due to exception");
                return false;
            }
        }
         
        #endregion
    }
}
