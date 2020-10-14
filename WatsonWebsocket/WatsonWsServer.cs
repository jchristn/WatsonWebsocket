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
    /// Watson Websocket server.
    /// </summary>
    public class WatsonWsServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Event fired when a client connects.
        /// </summary>
        public event EventHandler<ClientConnectedEventArgs> ClientConnected;

        /// <summary>
        /// Event fired when a client disconnects.
        /// </summary>
        public event EventHandler<ClientDisconnectedEventArgs> ClientDisconnected;

        /// <summary>
        /// Event fired when the server stops.
        /// </summary>
        public event EventHandler ServerStopped;

        /// <summary>
        /// Event fired when a message is received.
        /// </summary>
        public event EventHandler<MessageReceivedEventArgs> MessageReceived;

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
        /// Specify the IP addresses that are allowed to connect.  If none are supplied, all IP addresses are permitted.
        /// </summary>
        public List<string> PermittedIpAddresses = new List<string>();

        /// <summary>
        /// Method to invoke when sending a log message.
        /// </summary>
        public Action<string> Logger = null;

        /// <summary>
        /// Method to invoke when receiving a raw (non-websocket) HTTP request.
        /// </summary>
        public Action<HttpListenerContext> HttpHandler = null;

        /// <summary>
        /// Statistics.
        /// </summary>
        public Statistics Stats
        {
            get
            {
                return _Stats;
            }
        }

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
        private Task _AcceptConnectionsTask;
        private Statistics _Stats = new Statistics();

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
            else if (listenerIp == "*" || listenerIp == "+")
            {
                _ListenerIp = listenerIp;
                _ListenerIpAddress = IPAddress.Any;
            }
            else
            {
                if (!IPAddress.TryParse(listenerIp, out _ListenerIpAddress))
                {
                    _ListenerIpAddress = Dns.GetHostEntry(listenerIp).AddressList[0];
                }

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
            _Stats = new Statistics();

            Logger?.Invoke("[WatsonWsServer.Start] starting on " + _ListenerPrefix);

            if (_AcceptInvalidCertificates) ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;

            _AcceptConnectionsTask = Task.Run(AcceptConnections, _Token);
        }

        /// <summary>
        /// Send text data to the specified client, asynchronously.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="data">String containing data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string ipPort, string data)
        {
            if (String.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                Logger?.Invoke("[WatsonWsServer.SendAsync " + ipPort + "] unable to find client");
                return false;
            }

            return await MessageWriteAsync(client, Encoding.UTF8.GetBytes(data), WebSocketMessageType.Text);
        }

        /// <summary>
        /// Send binary data to the specified client, asynchronously.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="data">Byte array containing data.</param> 
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string ipPort, byte[] data)
        {
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                Logger?.Invoke("[WatsonWsServer.SendAsync " + ipPort + "] unable to find client");
                return false;
            }

            return await MessageWriteAsync(client, data, WebSocketMessageType.Binary);
        }

        /// <summary>
        /// Send binary data to the specified client, asynchronously.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="data">Byte array containing data.</param> 
        /// <param name="msgType">Web socket message type.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string ipPort, byte[] data, WebSocketMessageType msgType)
        {
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                Logger?.Invoke("[WatsonWsServer.SendAsync " + ipPort + "] unable to find client");
                return false;
            }

            return await MessageWriteAsync(client, data, msgType);
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
                client.TokenSource.Cancel();
            }
        }

        /// <summary>
        /// Retrieve the awaiter.
        /// </summary>
        /// <returns>TaskAwaiter.</returns>
        public TaskAwaiter GetAwaiter()
        {
            return _AcceptConnectionsTask.GetAwaiter();
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
          
        private async Task AcceptConnections()
        {
            string header = "[WatsonWsServer.AcceptConnections] ";

            try
            { 
                _Listener.Start();
                 
                while (!_Token.IsCancellationRequested)
                {  
                    HttpListenerContext ctx = await _Listener.GetContextAsync();
                    string ip = ctx.Request.RemoteEndPoint.Address.ToString();
                    int port = ctx.Request.RemoteEndPoint.Port;
                    string ipPort = ip + ":" + port;

                    lock (_PermittedIpsLock)
                    {
                        if (PermittedIpAddresses != null
                            && PermittedIpAddresses.Count > 0
                            && !PermittedIpAddresses.Contains(ip))
                        {
                            Logger?.Invoke(header + "rejecting connection from " + ipPort + " (not permitted)");
                            ctx.Response.StatusCode = 401;
                            ctx.Response.Close();
                            continue;
                        }
                    }
                     
                    if (!ctx.Request.IsWebSocketRequest)
                    {
                        if (HttpHandler == null)
                        {
                            Logger?.Invoke(header + "non-websocket request rejected from " + ipPort);
                            ctx.Response.StatusCode = 400;
                            ctx.Response.Close();
                        }
                        else
                        {
                            Logger?.Invoke(header + "non-websocket request forwarded to HTTP handler from " + ipPort + ": " + ctx.Request.HttpMethod.ToString() + " " + ctx.Request.RawUrl);
                            HttpHandler.Invoke(ctx);
                        }
                        
                        continue;
                    } 

                    CancellationTokenSource tokenSource = new CancellationTokenSource();
                    CancellationToken token = tokenSource.Token;

                    await Task.Run(() =>
                    {
                        Logger?.Invoke(header + "starting data receiver for " + ipPort);

                        Task.Run(async () =>
                        { 
                            WebSocketContext wsContext;
                            try
                            {
                                wsContext = await ctx.AcceptWebSocketAsync(subProtocol: null);
                            }
                            catch (Exception)
                            {
                                Logger?.Invoke(header + "unable to retrieve websocket content for client " + ipPort);
                                ctx.Response.StatusCode = 500;
                                ctx.Response.Close();
                                return;
                            }

                            WebSocket ws = wsContext.WebSocket;
                            ClientMetadata md = new ClientMetadata(ctx, ws, wsContext, tokenSource);
                             
                            _Clients.TryAdd(md.IpPort, md);
                            ClientConnected?.Invoke(this, new ClientConnectedEventArgs(md.IpPort, ctx.Request));
                            await Task.Run(() => DataReceiver(md), token);
                             
                        }, token);

                    }, _Token); 
                } 
            }
            catch (HttpListenerException)
            {
                // can be thrown when disposed
            }
            catch (OperationCanceledException)
            {
                // thrown when disposed
            }
            catch (Exception e)
            {
                Logger?.Invoke(header + "exception:" + Environment.NewLine + e.ToString());
            }
            finally
            {
                ServerStopped?.Invoke(this, EventArgs.Empty);
            }
        }

        private async Task DataReceiver(ClientMetadata md)
        { 
            string header = "[WatsonWsServer.DataReceiver " + md.IpPort + "] ";
            Logger?.Invoke(header + "starting data receiver");
             
            try
            { 
                while (true)
                {
                    MessageReceivedEventArgs msg = await MessageReadAsync(md);

                    _Stats.ReceivedMessages = _Stats.ReceivedMessages + 1;
                    _Stats.ReceivedBytes += msg.Data.Length;

                    if (msg.Data != null)
                    { 
                        MessageReceived?.Invoke(this, msg);
                    }
                    else
                    { 
                        await Task.Delay(100);
                    }
                }
            }  
            catch (OperationCanceledException)
            {
                // thrown when disposed
            }
            catch (WebSocketException)
            {
                Logger?.Invoke(header + "websocket disconnected");
            } 
            catch (Exception e)
            { 
                Logger?.Invoke(header + "exception: " + Environment.NewLine + e.ToString());
            }
            finally
            { 
                string ipPort = md.IpPort;
                ClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs(md.IpPort));
                md.Ws.Dispose();
                Logger?.Invoke(header + "disconnected");
                _Clients.TryRemove(ipPort, out _);
            }
        }
          
        private async Task<MessageReceivedEventArgs> MessageReadAsync(ClientMetadata md)
        { 
            using (MemoryStream stream = new MemoryStream())
            {
                byte[] buffer = new byte[65536];
                ArraySegment<byte> seg = new ArraySegment<byte>(buffer);

                while (true)
                {
                    WebSocketReceiveResult result = await md.Ws.ReceiveAsync(seg, md.TokenSource.Token);
                    if (result.CloseStatus != null
                        || md.Ws.State != WebSocketState.Open
                        || result.MessageType == WebSocketMessageType.Close
                        || md.TokenSource.Token.IsCancellationRequested)
                    {
                        throw new WebSocketException("Websocket closed.");
                    }

                    if (result.Count > 0)
                    {
                        stream.Write(buffer, 0, result.Count);
                    }

                    if (result.EndOfMessage)
                    {
                        return new MessageReceivedEventArgs(md.IpPort, stream.ToArray(), result.MessageType);
                    }
                }
            } 
        }
 
        private async Task<bool> MessageWriteAsync(ClientMetadata md, byte[] data, WebSocketMessageType msgType)
        {
            string header = "[WatsonWsServer.MessageWriteAsync " + md.IpPort + "] ";

            try
            {
                #region Send-Message

                // Cannot have two simultaneous SendAsync calls so use a 
                // semaphore to block the second until the first has completed

                await md.SendLock.WaitAsync(md.TokenSource.Token);
                try
                {
                    await md.Ws.SendAsync(new ArraySegment<byte>(data, 0, data.Length), msgType, true, md.TokenSource.Token);
                }
                finally
                {
                    md.SendLock.Release();
                }

                _Stats.SentMessages += 1;
                _Stats.SentBytes += data.Length;

                return true;

                #endregion
            }
            catch (OperationCanceledException oce)
            {
                Logger?.Invoke(header + "disconnected (canceled): " + oce.Message);
            }
            catch (WebSocketException wse)
            {
                Logger?.Invoke(header + "disconnected (websocket exception): " + wse.Message);
            }
            catch (Exception e)
            {
                Logger?.Invoke(header + "disconnected due to exception: " + Environment.NewLine + e.ToString()); 
            }

            return false;
        }
         
        #endregion
    }
}
