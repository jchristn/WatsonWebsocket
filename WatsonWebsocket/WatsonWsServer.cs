using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
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

        /// <summary>
        /// Callback called when a client connects.  Return 'true' to permit the connection or 'false' to deny.
        /// Parameter 1: string containing "[ip]:[port]".
        /// Parameter 2: full HttpListenerRequest object.
        /// </summary>
        public Func<string, HttpListenerRequest, Task<bool>> ClientConnected = null;

        /// <summary>
        /// Callback called when a client disconnects.
        /// Parameter 1: string containing "[ip]:[port]".
        /// </summary>
        public Func<string, Task> ClientDisconnected = null;

        /// <summary>
        /// Callback called when the server stops.
        /// </summary>
        public Func<Task> ServerStopped = null;

        /// <summary>
        /// Callback called when a message is received.
        /// Parameter 1: string containing "[ip]:[port]".
        /// Parameter 2: byte array containing the data.
        /// </summary>
        public Func<string, byte[], Task> MessageReceived = null;

        /// <summary>
        /// Indicate whether or not invalid or otherwise unverifiable certificates should be accepted.  Default is true.
        /// </summary>
        public bool AcceptInvalidCertificates
        {
            get
            {
                return _AcceptInvalidCertificates;
            }
            set
            {
                _AcceptInvalidCertificates = value;
            }
        }

        /// <summary>
        /// Enable or disable console debugging.
        /// </summary>
        public bool Debug = false;

        /// <summary>
        /// Specify the IP addresses that are allowed to connect.  If none are supplied, all IP addresses are permitted.
        /// </summary>
        public List<string> PermittedIpAddresses = new List<string>();

        #endregion

        #region Private-Members
         
        private bool _AcceptInvalidCertificates = true;
        private string _ListenerIp;
        private int _ListenerPort;
        private IPAddress _ListenerIpAddress;
        private string _ListenerPrefix;
        private HttpListener _Listener;
        private readonly object _PermittedIpsLock = new object();
        private ConcurrentDictionary<string, ClientMetadata> _Clients; 
        private CancellationTokenSource _TokenSource;
        private CancellationToken _Token; 
        private Task _AsyncTask;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes the Watson websocket server.
        /// Be sure to call 'Start()' to start the server.
        /// </summary>
        /// <param name="listenerIp">The IP address upon which to listen.</param>
        /// <param name="listenerPort">The TCP port upon which to listen.</param>
        /// <param name="ssl">Enable or disable SSL.</param> 
        public WatsonWsServer(
            string listenerIp,
            int listenerPort,
            bool ssl)
        {
            if (listenerPort < 1) throw new ArgumentOutOfRangeException(nameof(listenerPort));
             
            if (String.IsNullOrEmpty(listenerIp))
            {
                _ListenerIpAddress = IPAddress.Loopback;
                _ListenerIp = _ListenerIpAddress.ToString();
            }
            else if (listenerIp == "*")
            {
                _ListenerIp = "*";
                _ListenerIpAddress = IPAddress.Any;
            }
            else
            {
                _ListenerIpAddress = IPAddress.Parse(listenerIp);
                _ListenerIp = listenerIp;
            }

            _ListenerPort = listenerPort; 

            if (ssl) _ListenerPrefix = "https://" + _ListenerIp + ":" + _ListenerPort + "/";
            else _ListenerPrefix = "http://" + _ListenerIp + ":" + _ListenerPort + "/";

            _Listener = new HttpListener();
            _Listener.Prefixes.Add(_ListenerPrefix);

            _TokenSource = new CancellationTokenSource();
            _Token = _TokenSource.Token;
            _Clients = new ConcurrentDictionary<string, ClientMetadata>();
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
        /// Start the server.
        /// </summary>
        public void Start()
        {
            Log("WatsonWsServer starting on " + _ListenerPrefix);

            if (_AcceptInvalidCertificates) ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;

            _AsyncTask = Task.Run(AcceptConnections, _Token);
        }

        /// <summary>
        /// Send data to the specified client, asynchronously.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="data">String containing data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string ipPort, string data)
        {
            return await SendAsync(ipPort, Encoding.UTF8.GetBytes(data), WebSocketMessageType.Text);
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
            if (!_Clients.TryGetValue(ipPort, out client))
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
            return _Clients.TryGetValue(ipPort, out client);
        }

        /// <summary>
        /// List the IP:port of each connected client.
        /// </summary>
        /// <returns>A string list containing each client IP:port.</returns>
        public IEnumerable<string> ListClients()
        {
            return _Clients.Keys.ToArray();
        }

        /// <summary>
        /// Forcefully disconnect a client.
        /// </summary>
        /// <param name="ipPort">IP:port of the client.</param>
        public void DisconnectClient(string ipPort)
        {
            // force disconnect of client
            if (_Clients.TryGetValue(ipPort, out var client))
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
            return _AsyncTask.GetAwaiter();
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Tear down the server and dispose of background workers.
        /// </summary>
        /// <param name="disposing">Disposing.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _Listener?.Stop();
                _TokenSource.Cancel();
            }
        }

        private void Log(string msg)
        {
            if (Debug) Console.WriteLine(msg);
        }

        private void LogException(string method, Exception e)
        {
            Log("");
            Log("An exception was encountered.");
            Log("   Method      : " + method);
            Log("   Type        : " + e.GetType().ToString());
            Log("   Data        : " + e.Data);
            Log("   Inner       : " + e.InnerException);
            Log("   Message     : " + e.Message);
            Log("   Source      : " + e.Source);
            Log("   StackTrace  : " + e.StackTrace);
            Log("");
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
                 
                _Listener.Start();
                 
                while (!_Token.IsCancellationRequested)
                { 
                    #region Accept-and-Verify-Connection

                    HttpListenerContext ctx = await _Listener.GetContextAsync();
                         
                    string clientIp = ctx.Request.RemoteEndPoint.Address.ToString();
                    int clientPort = ctx.Request.RemoteEndPoint.Port;
                    string ipPort = clientIp + ":" + clientPort;

                    lock (_PermittedIpsLock)
                    {
                        if (PermittedIpAddresses != null
                            && PermittedIpAddresses.Count > 0
                            && !PermittedIpAddresses.Contains(clientIp))
                        {
                            Log("*** AcceptConnections rejecting connection from " + clientIp + " (not permitted)");
                            ctx.Response.StatusCode = 401;
                            ctx.Response.Close();
                            continue;
                        }
                    }

                    Log("AcceptConnections accepted connection from " + clientIp + ":" + clientPort);
                         
                    if (!ctx.Request.IsWebSocketRequest)
                    {
                        Log("*** AcceptConnections rejecting connection from " + clientIp + " (not a websocket request)");
                        ctx.Response.StatusCode = 400;
                        ctx.Response.Close();
                        continue;
                    }

                    #endregion

                    #region Start-Client-Task

                    CancellationTokenSource killTs = new CancellationTokenSource();
                    CancellationToken killToken = killTs.Token;

                    Task _ = Task.Run(() =>
                    {

                        Log("AcceptConnections starting data receiver for " + ipPort + " (now " + _Clients.Count + " clients)");

                        Task.Run(async () =>
                        {
                            #region Connection-Callback

                            if (ClientConnected != null)
                            {
                                bool connected = await ClientConnected(ipPort, ctx.Request);

                                if (!connected)
                                {
                                    Log("*** AcceptConnections unable to validate client " + ipPort);
                                    ctx.Response.StatusCode = 401;
                                    ctx.Response.Close();
                                    return;
                                }
                            }

                            #endregion

                            #region Get-Websocket-Context

                            WebSocketContext wsContext;
                            try
                            {
                                wsContext = await ctx.AcceptWebSocketAsync(subProtocol: null);
                            }
                            catch (Exception)
                            {
                                Log("*** AcceptConnections unable to retrieve websocket content for client " + ipPort);
                                ctx.Response.StatusCode = 500;
                                ctx.Response.Close();
                                return;
                            }

                            WebSocket ws = wsContext.WebSocket;
                            ClientMetadata client = new ClientMetadata(ctx, ws, wsContext, killTs);

                            #endregion

                            #region Add-Client

                            if (!AddClient(client))
                            {
                                Log("*** AcceptConnections unable to add client " + ipPort);
                                ctx.Response.StatusCode = 500;
                                ctx.Response.Close();
                                return;
                            }

                            await DataReceiver(client, killToken);

                            #endregion

                        }, killToken);

                    }, _Token);

                    #endregion 
                }

                #endregion
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception e)
            {
                LogException("AcceptConnections", e);
            }
            finally
            {
                ServerStopped?.Invoke();
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
                            var _ = Task.Run(() => MessageReceived?.Invoke(client.IpPort(), data), CancellationToken.None);
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
                    client.Ws.Dispose();
                    Log("DataReceiver client " + clientId + " disconnected (now " + _Clients.Count + " clients active)");
                }
            }
        }

        private bool AddClient(ClientMetadata client)
        { 
            _Clients.TryRemove(client.IpPort(), out var removedClient);
            return _Clients.TryAdd(client.IpPort(), client);
        }

        private bool RemoveClient(ClientMetadata client)
        { 
            return _Clients.TryRemove(client.IpPort(), out var removedClient);
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
