using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks; 

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
        private readonly ConcurrentDictionary<string, ClientMetadata> Clients;
        private readonly ConcurrentDictionary<string, bool> PermittedIps;
        private readonly CancellationTokenSource TokenSource;
        private CancellationToken Token;
        private readonly Func<string, IDictionary<string, string>, bool> ClientConnected;
        private readonly Func<string, bool> ClientDisconnected;
        private readonly Func<string, byte[], bool> MessageReceived;
        private readonly Task AsyncTask;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes the Watson websocket server.
        /// </summary>
        /// <param name="listenerIp">The IP address upon which to listen.</param>
        /// <param name="listenerPort">The TCP port upon which to listen.</param>
        /// <param name="ssl">Enable or disable SSL.</param>
        /// <param name="acceptInvalidCerts">Enable or disable acceptance of certificates that cannot be validated.</param>
        /// <param name="permittedIps">List of strings containing permitted client IP addresses.</param>
        /// <param name="clientConnected">Function to call when a client connects.</param>
        /// <param name="clientDisconnected">Function to call when a client disconnects.</param>
        /// <param name="messageReceived">Function to call when a message is received from a client.</param>
        /// <param name="debug">Enable or disable verbose console logging.</param>
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

            ClientConnected = clientConnected;
            ClientDisconnected = clientDisconnected;
            PermittedIps = null;

            MessageReceived = messageReceived ?? throw new ArgumentNullException(nameof(MessageReceived));
            Debug = debug;

            if (permittedIps != null && permittedIps.Any())
            {
                PermittedIps = new ConcurrentDictionary<string, bool>();
                foreach (var ip in permittedIps)
                {
                    PermittedIps[ip] = true;
                }
            }

            if (String.IsNullOrEmpty(listenerIp))
            {
                ListenerIpAddress = IPAddress.Loopback;
                ListenerIp = ListenerIpAddress.ToString();
            }
            else if (listenerIp == "*")
            {
                ListenerIp = "*";
                ListenerIpAddress = IPAddress.Any;
            }
            else
            {
                ListenerIpAddress = IPAddress.Parse(listenerIp);
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
            Token = TokenSource.Token;
            Clients = new ConcurrentDictionary<string, ClientMetadata>();
            AsyncTask = Task.Run(AcceptConnections, Token);
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
            return await SendAsync(ipPort, data, WebSocketMessageType.Binary);
        }

        /// <summary>
        /// Send data to the specified client, asynchronously.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="data">Byte array containing data.</param>
        /// <param name="messageType">The type of websocket message.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string ipPort, byte[] data, WebSocketMessageType messageType)
        { 
            ClientMetadata client;
            if (!Clients.TryGetValue(ipPort, out client))
            {
                Log("Send unable to find client " + ipPort);
                return false;
            }

            return await MessageWriteAsync(client, data, messageType);
        }

        /// <summary>
        /// Determine whether or not the specified client is connected to the server.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <returns>Boolean indicating if the client is connected to the server.</returns>
        public bool IsClientConnected(string ipPort)
        {
            ClientMetadata client;
            return Clients.TryGetValue(ipPort, out client);
        }

        /// <summary>
        /// List the IP:port of each connected client.
        /// </summary>
        /// <returns>A string list containing each client IP:port.</returns>
        public IEnumerable<string> ListClients()
        {
            return Clients.Keys.ToArray();
        }

        /// <summary>
        /// Forcefully disconnect a client.
        /// </summary>
        /// <param name="ipPort">IP:port of the client.</param>
        public void KillClient(string ipPort)
        {
            // force disconnect of client
            if (Clients.TryGetValue(ipPort, out var client))
            {
                client.KillToken.Cancel();
            }
        }

        /// <summary>
        /// Retrieve the awaiter.
        /// </summary>
        /// <returns>TaskAwaiter.</returns>
        public TaskAwaiter GetAwaiter()
        {
            return AsyncTask.GetAwaiter();
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
                
                while (!Token.IsCancellationRequested)
                { 
                    #region Accept-Connection

                    HttpListenerContext httpContext = await Listener.GetContextAsync();

                    #endregion
                     
                    if (httpContext.Request.RemoteEndPoint != null)
                    {
                        #region Check-IP-Address

                        string clientIp = httpContext.Request.RemoteEndPoint.Address.ToString();
                        int clientPort = httpContext.Request.RemoteEndPoint.Port;

                        Dictionary<string, string> query = new Dictionary<string, string>();
                        NameValueCollection requestQuery = httpContext.Request.QueryString;
                        foreach (string key in requestQuery.AllKeys)
                        {
                            query[key] = requestQuery[key];
                        }

                        if (PermittedIps != null && PermittedIps.Count > 0)
                        {
                            if (!PermittedIps.ContainsKey(clientIp))
                            {
                                Log("*** AcceptConnections rejecting connection from " + clientIp + " (not permitted)");
                                httpContext.Response.StatusCode = 401;
                                httpContext.Response.Close();
                                continue;
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
                            continue;
                        }

                        WebSocket ws = wsContext.WebSocket;

                        #endregion

                        Task unawaited = Task.Run(() =>
                        { 
                            #region Add-to-Client-List
                            
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
                            CancellationToken killToken = currClient.KillToken.Token;

                            Log("AcceptConnections starting data receiver for " + clientIp + ":" + clientPort + " (now " + Clients.Count + " clients)");
                            if (ClientConnected != null)
                            {
                                Task.Run(() => ClientConnected(clientIp + ":" + clientPort, query), killToken);
                            }

                            Task.Run(async () => await DataReceiver(currClient, killToken), killToken);

                            #endregion 

                        }, Token);
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
            var clientId = client.IpPort();
            try
            {
                #region Wait-for-Data

                while (true)
                {
                    cancelToken?.ThrowIfCancellationRequested();
                    
                    byte[] data = await MessageReadAsync(client);
                    if (data != null)
                    {
                        if (MessageReceived != null)
                        {
                            var unawaited = Task.Run(() => MessageReceived?.Invoke(client.IpPort(), data),
                                CancellationToken.None);
                        }
                    }
                    else
                    {
                        // no message available
                        await Task.Delay(30, cancelToken.GetValueOrDefault());
                    }
                }

                #endregion
            }
            catch (OperationCanceledException oce)
            {
                Log("DataReceiver client " + clientId + " disconnected (canceled): " + oce.Message); 
            }
            catch (WebSocketException wse)
            {
                Log("DataReceiver client " + clientId + " disconnected (websocket exception): " + wse.Message);
            }
            finally
            {
                if (RemoveClient(client))
                {
                    // must only fire disconnected event if the client was previously connected. Note that
                    // multithreading gives multiple disconnection events from the socket, the reader and the writer
                    ClientDisconnected?.Invoke(clientId);
                    Log("DataReceiver client " + clientId + " disconnected (now " + Clients.Count + " clients active)");
                }
            }
        }

        private bool AddClient(ClientMetadata client)
        { 
            Clients.TryRemove(client.IpPort(), out var removedClient);
            return Clients.TryAdd(client.IpPort(), client);
        }

        private bool RemoveClient(ClientMetadata client)
        { 
            return Clients.TryRemove(client.IpPort(), out var removedClient);
        }

        private async Task<byte[]> MessageReadAsync(ClientMetadata client)
        {
            /*
             *
             * Do not catch exceptions, let them get caught by the data reader
             * to destroy the connection
             *
             */

            #region Check-for-Null-Values

            if (client.HttpContext == null) return null;
            if (client.WsContext == null) return null;

            #endregion

            #region Variables

            byte[] contentBytes;

            #endregion
             
            #region Read-Data

            using (var dataStream = new MemoryStream())
            {
                const long bufferSize = 16*1024;

                var buffer = new byte[bufferSize];
                var bufferSegment = new ArraySegment<byte>(buffer);
                while (client.Ws.State == WebSocketState.Open)
                {
                    var receiveResult = await client.Ws.ReceiveAsync(bufferSegment, client.KillToken.Token);
                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        throw new WebSocketException("Socket closed");
                    }

                    // write this chunk to the data stream
                    dataStream.Write(buffer, 0, receiveResult.Count);
                    if (receiveResult.EndOfMessage)
                    {
                        // end of message so return the buffer
                        break;
                    }
                }

                contentBytes = dataStream.ToArray();
            }

            #endregion

            return contentBytes.Length == 0 ? null : contentBytes;
        }

        private async Task<bool> MessageWriteAsync(ClientMetadata client, byte[] data, WebSocketMessageType messageType)
        { 
            try
            {
                #region Send-Message

                // Cannot have two simultaneous SendAsync calls so use a 
                // semaphore to block the second until the first has completed

                await client.SendAsyncLock.WaitAsync(client.KillToken.Token);
                try
                {
                    await client.Ws.SendAsync(new ArraySegment<byte>(data, 0, data.Length),
                        messageType, true, client.KillToken.Token);
                }
                finally
                {
                    client.SendAsyncLock.Release();
                }

                return true;

                #endregion
            }
            catch (Exception e)
            {
                Log("*** MessageWriteAsync " + client.IpPort() + " disconnected due to exception "+e);
                return false;
            }
        }
         
        #endregion
    }
}
