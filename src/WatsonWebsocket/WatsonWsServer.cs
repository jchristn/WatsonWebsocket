using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
        /// Determine if the server is listening for new connections.
        /// </summary>
        public bool IsListening
        {
            get
            {
                if (_Listener != null)
                {
                    return _Listener.IsListening;
                }

                return false;
            }
        }

        /// <summary>
        /// Enable or disable statistics.
        /// </summary>
        public bool EnableStatistics { get; set; } = true;

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

        private string _Header = "[WatsonWsServer] ";
        private bool _AcceptInvalidCertificates = true;
        private List<string> _ListenerPrefixes = new List<string>();
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
        /// Initializes the Watson websocket server with a single listener prefix.
        /// Be sure to call 'Start()' to start the server.
        /// By default, Watson Websocket will listen on http://localhost:9000/.
        /// </summary>
        /// <param name="hostname">The hostname or IP address upon which to listen.</param>
        /// <param name="port">The TCP port on which to listen.</param>
        /// <param name="ssl">Enable or disable SSL.</param> 
        public WatsonWsServer(string hostname = "localhost", int port = 9000, bool ssl = false)
        {
            if (port < 0) throw new ArgumentOutOfRangeException(nameof(port));
            if (String.IsNullOrEmpty(hostname)) hostname = "localhost";
            
            if (ssl) _ListenerPrefixes.Add("https://" + hostname + ":" + port + "/");
            else _ListenerPrefixes.Add("http://" + hostname + ":" + port + "/");

            _Listener = new HttpListener();
            foreach (string prefix in _ListenerPrefixes)
            {
                _Listener.Prefixes.Add(prefix);
            }

            _TokenSource = new CancellationTokenSource();
            _Token = _TokenSource.Token;
            _Clients = new ConcurrentDictionary<string, ClientMetadata>();
        }

        /// <summary>
        /// Initializes the Watson websocket server with one or more listener prefixes.  
        /// Be sure to call 'Start()' to start the server.
        /// </summary>
        /// <param name="hostnames">The hostnames or IP addresses upon which to listen.</param>
        /// <param name="port">The TCP port on which to listen.</param>
        /// <param name="ssl">Enable or disable SSL.</param>
        public WatsonWsServer(List<string> hostnames, int port, bool ssl = false)
        {
            if (port < 0) throw new ArgumentOutOfRangeException(nameof(port));
            if (hostnames == null) throw new ArgumentNullException(nameof(hostnames));
            if (hostnames.Count < 1) throw new ArgumentException("At least one hostname must be supplied.");

            foreach (string hostname in hostnames)
            {
                if (ssl) _ListenerPrefixes.Add("https://" + hostname + ":" + port + "/");
                else _ListenerPrefixes.Add("http://" + hostname + ":" + port + "/");
            }

            _Listener = new HttpListener();
            foreach (string prefix in _ListenerPrefixes)
            {
                _Listener.Prefixes.Add(prefix);
            }

            _TokenSource = new CancellationTokenSource();
            _Token = _TokenSource.Token;
            _Clients = new ConcurrentDictionary<string, ClientMetadata>();
        }

        /// <summary>
        /// Initializes the Watson websocket server.
        /// Be sure to call 'Start()' to start the server.
        /// </summary>
        /// <param name="uri">The URI on which you wish to listen, i.e. http://localhost:9090.</param>
        public WatsonWsServer(Uri uri)
        {
            if (uri == null) throw new ArgumentNullException(nameof(uri));

            if (uri.Port < 0) throw new ArgumentException("Port must be zero or greater.");

            string host;
            if (!IPAddress.TryParse(uri.Host, out _))
            {
                var dnsLookup = Dns.GetHostEntry(uri.Host);
                if (dnsLookup.AddressList.Length > 0)
                {
                    host = dnsLookup.AddressList.First().ToString();
                }
                else
                {
                    throw new ArgumentException("Cannot resolve address to IP.");
                }
            }
            else
            {
                host = uri.Host;
            }

            var listenerUri = new UriBuilder(uri)
            {
                Host = host
            };

            _ListenerPrefixes.Add(listenerUri.ToString());

            _Listener = new HttpListener();
            foreach (string prefix in _ListenerPrefixes)
            {
                _Listener.Prefixes.Add(prefix);
            }

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
        /// Start accepting new connections.
        /// </summary>
        public void Start()
        {
            if (IsListening) throw new InvalidOperationException("Watson websocket server is already running.");

            _Stats = new Statistics();

            string logMsg = _Header + "starting on:";
            foreach (string prefix in _ListenerPrefixes) logMsg += " " + prefix;
            Logger?.Invoke(logMsg);

            if (_AcceptInvalidCertificates) SetInvalidCertificateAcceptance();

            _TokenSource = new CancellationTokenSource();
            _Token = _TokenSource.Token;
            _Listener.Start();

            _AcceptConnectionsTask = Task.Run(() => AcceptConnections(_Token), _Token);
        }

        /// <summary>
        /// Start accepting new connections.
        /// </summary>
        /// <returns>Task.</returns>
        public Task StartAsync(CancellationToken token = default)
        {
            if (IsListening) throw new InvalidOperationException("Watson websocket server is already running.");

            _Stats = new Statistics();

            string logMsg = _Header + "starting on:";
            foreach (string prefix in _ListenerPrefixes) logMsg += " " + prefix;
            Logger?.Invoke(logMsg);

            if (_AcceptInvalidCertificates) SetInvalidCertificateAcceptance();

            _TokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            _Token = token;

            _Listener.Start();

            _AcceptConnectionsTask = Task.Run(() => AcceptConnections(_Token), _Token);

            return Task.Delay(1);
        }

        /// <summary>
        /// Stop accepting new connections.
        /// </summary>
        public void Stop()
        {
            if (!IsListening) throw new InvalidOperationException("Watson websocket server is not running.");

            Logger?.Invoke(_Header + "stopping");

            _Listener.Stop(); 
        }

        /// <summary>
        /// Send text data to the specified client, asynchronously.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="data">String containing data.</param>
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public Task<bool> SendAsync(string ipPort, string data, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                Logger?.Invoke(_Header + "unable to find client " + ipPort);
                return Task.FromResult(false);
            }

            Task<bool> task = MessageWriteAsync(client, new ArraySegment<byte>(Encoding.UTF8.GetBytes(data)), WebSocketMessageType.Text, token);

            client = null;
            return task;
        }

        /// <summary>
        /// Send binary data to the specified client, asynchronously.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="data">Byte array containing data.</param> 
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public Task<bool> SendAsync(string ipPort, byte[] data, CancellationToken token = default)
        {
            return SendAsync(ipPort, new ArraySegment<byte>(data), WebSocketMessageType.Binary, token);
        }

        /// <summary>
        /// Send binary data to the specified client, asynchronously.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="data">Byte array containing data.</param> 
        /// <param name="msgType">Web socket message type.</param>
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public Task<bool> SendAsync(string ipPort, byte[] data, WebSocketMessageType msgType, CancellationToken token = default)
        {
            return SendAsync(ipPort, new ArraySegment<byte>(data), msgType, token);
        }

        /// <summary>
        /// Send binary data to the specified client, asynchronously.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="data">ArraySegment containing data.</param> 
        /// <param name="msgType">Web socket message type.</param>
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public Task<bool> SendAsync(string ipPort, ArraySegment<byte> data, WebSocketMessageType msgType = WebSocketMessageType.Binary, CancellationToken token = default)
        {
            if (data.Array == null || data.Count < 1) throw new ArgumentNullException(nameof(data));
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                Logger?.Invoke(_Header + "unable to find client " + ipPort);
                return Task.FromResult(false);
            }

            Task<bool> task = MessageWriteAsync(client, data, msgType, token);

            client = null;
            return task;
        }

        /// <summary>
        /// Determine whether or not the specified client is connected to the server.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <returns>Boolean indicating if the client is connected to the server.</returns>
        public bool IsClientConnected(string ipPort)
        {
            return _Clients.TryGetValue(ipPort, out _);
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
            if (_Clients.TryGetValue(ipPort, out var client))
            {
                lock (client)
                {
                    // lock because CloseOutputAsync can fail with InvalidOperationAsync with overlapping operations
                    client.Ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", client.TokenSource.Token).Wait();
                    client.TokenSource.Cancel();
                    client.Ws.Dispose();
                }
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
                if (_Clients != null)
                {
                    foreach (KeyValuePair<string, ClientMetadata> client in _Clients)
                    {
                        client.Value.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", client.Value.TokenSource.Token);
                        client.Value.TokenSource.Cancel();
                    }
                }

                if (_Listener != null)
                {
                    if (_Listener.IsListening) _Listener.Stop();
                    _Listener.Close();
                }

                _TokenSource.Cancel();
            }
        }

        private void SetInvalidCertificateAcceptance()
        {
#if NETFRAMEWORK
            ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;
#endif

#if NET || NETSTANDARD || NETCOREAPP
            ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;
#endif
        }

        private async Task AcceptConnections(CancellationToken cancelToken)
        { 
            try
            { 
                while (!cancelToken.IsCancellationRequested)
                {
                    if (!_Listener.IsListening)
                    {
                        Task.Delay(100).Wait();
                        continue;
                    } 

                    HttpListenerContext ctx = await _Listener.GetContextAsync().ConfigureAwait(false);
                    string ip = ctx.Request.RemoteEndPoint.Address.ToString();
                    int port = ctx.Request.RemoteEndPoint.Port;
                    string ipPort = ip + ":" + port;

                    lock (_PermittedIpsLock)
                    {
                        if (PermittedIpAddresses != null
                            && PermittedIpAddresses.Count > 0
                            && !PermittedIpAddresses.Contains(ip))
                        {
                            Logger?.Invoke(_Header + "rejecting " + ipPort + " (not permitted)");
                            ctx.Response.StatusCode = 401;
                            ctx.Response.Close();
                            continue;
                        }
                    }
                     
                    if (!ctx.Request.IsWebSocketRequest)
                    {
                        if (HttpHandler == null)
                        {
                            Logger?.Invoke(_Header + "non-websocket request rejected from " + ipPort);
                            ctx.Response.StatusCode = 400;
                            ctx.Response.Close();
                        }
                        else
                        {
                            Logger?.Invoke(_Header + "non-websocket request from " + ipPort + " HTTP-forwarded: " + ctx.Request.HttpMethod.ToString() + " " + ctx.Request.RawUrl);
                            HttpHandler.Invoke(ctx);
                        }
                        
                        continue;
                    } 
                    else
                    { 
                        /*
                        HttpListenerRequest req = ctx.Request;
                        Console.WriteLine(Environment.NewLine + req.HttpMethod.ToString() + " " + req.RawUrl);
                        if (req.Headers != null && req.Headers.Count > 0)
                        {
                            Console.WriteLine("Headers:");
                            var items = req.Headers.AllKeys.SelectMany(req.Headers.GetValues, (k, v) => new { key = k, value = v });
                            foreach (var item in items)
                            {
                                Console.WriteLine("  {0}: {1}", item.key, item.value);
                            }
                        } 
                        */
                    }

                    await Task.Run(() =>
                    {
                        Logger?.Invoke(_Header + "starting data receiver for " + ipPort);

                        CancellationTokenSource tokenSource = new CancellationTokenSource();
                        CancellationToken token = tokenSource.Token;

                        Task.Run(async () =>
                        {
                            WebSocketContext wsContext = await ctx.AcceptWebSocketAsync(subProtocol: null);
                            WebSocket ws = wsContext.WebSocket;
                            ClientMetadata md = new ClientMetadata(ctx, ws, wsContext, tokenSource);
                             
                            _Clients.TryAdd(md.IpPort, md);

                            ClientConnected?.Invoke(this, new ClientConnectedEventArgs(md.IpPort, ctx.Request));
                            await Task.Run(() => DataReceiver(md), token);
                             
                        }, token);

                    }, _Token).ConfigureAwait(false); 
                } 
            }
            /*
            catch (HttpListenerException)
            {
                // thrown when disposed
            }
            */
            catch (TaskCanceledException)
            {
                // thrown when disposed
            }
            catch (OperationCanceledException)
            {
                // thrown when disposed
            }
            catch (ObjectDisposedException)
            {
                // thrown when disposed
            }
            catch (Exception e)
            {
                Logger?.Invoke(_Header + "listener exception:" + Environment.NewLine + e.ToString());
            }
            finally
            {
                ServerStopped?.Invoke(this, EventArgs.Empty);
            }
        }

        private async Task DataReceiver(ClientMetadata md)
        { 
            string header = "[WatsonWsServer " + md.IpPort + "] ";
            Logger?.Invoke(header + "starting data receiver");
            byte[] buffer = new byte[65536];

            try
            { 
                while (true)
                {
                    MessageReceivedEventArgs msg = await MessageReadAsync(md, buffer).ConfigureAwait(false);

                    if (msg != null)
                    {
                        if (EnableStatistics)
                        {
                            _Stats.IncrementReceivedMessages();
                            _Stats.AddReceivedBytes(msg.Data.Count);
                        }

                        if (msg.Data != null)
                        {
                            Task unawaited = Task.Run(() => MessageReceived?.Invoke(this, msg), md.TokenSource.Token);
                        }
                        else
                        {
                            await Task.Delay(10).ConfigureAwait(false);
                        }
                    }
                }
            }  
            catch (TaskCanceledException)
            {
                // thrown when disposed
            }
            catch (OperationCanceledException)
            {
                // thrown when disposed
            }
            catch (WebSocketException)
            {
                // thrown by MessageReadAsync
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
         
        private async Task<MessageReceivedEventArgs> MessageReadAsync(ClientMetadata md, byte[] buffer)
        {
            string header = "[WatsonWsServer " + md.IpPort + "] ";

            using (MemoryStream ms = new MemoryStream())
            {
                ArraySegment<byte> seg = new ArraySegment<byte>(buffer);

                while (true)
                {
                    WebSocketReceiveResult result = await md.Ws.ReceiveAsync(seg, md.TokenSource.Token).ConfigureAwait(false);
                    if (result.CloseStatus != null)
                    {
                        Logger?.Invoke(header + "close received");
                        await md.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        throw new WebSocketException("Websocket closed.");
                    }

                    if (md.Ws.State != WebSocketState.Open)
                    {
                        Logger?.Invoke(header + "websocket no longer open");
                        throw new WebSocketException("Websocket closed.");
                    }

                    if (md.TokenSource.Token.IsCancellationRequested)
                    {
                        Logger?.Invoke(header + "cancel requested");
                    }

                    if (result.Count > 0)
                    {
                        ms.Write(buffer, 0, result.Count);
                    }

                    if (result.EndOfMessage)
                    {
                        return new MessageReceivedEventArgs(md.IpPort, new ArraySegment<byte>(ms.GetBuffer(), 0, (int)ms.Length), result.MessageType);
                    }
                }
            } 
        }
 
        private async Task<bool> MessageWriteAsync(ClientMetadata md, ArraySegment<byte> data, WebSocketMessageType msgType, CancellationToken token)
        {
            string header = "[WatsonWsServer " + md.IpPort + "] ";

            CancellationToken[] tokens = new CancellationToken[3];
            tokens[0] = _Token;
            tokens[1] = token;
            tokens[2] = md.TokenSource.Token;

            using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(tokens))
            {
                try
                {
                    #region Send-Message

                    await md.SendLock.WaitAsync(md.TokenSource.Token).ConfigureAwait(false);

                    try
                    {
                        await md.Ws.SendAsync(data, msgType, true, linkedCts.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        md.SendLock.Release();
                    }

                    if (EnableStatistics)
                    {
                        _Stats.IncrementSentMessages();
                        _Stats.AddSentBytes(data.Count);
                    }

                    return true;

                    #endregion
                }
                catch (TaskCanceledException)
                {
                    if (_Token.IsCancellationRequested)
                    {
                        Logger?.Invoke(header + "server canceled");
                    }
                    else if (token.IsCancellationRequested)
                    {
                        Logger?.Invoke(header + "message send canceled");
                    }
                    else if (md.TokenSource.Token.IsCancellationRequested)
                    {
                        Logger?.Invoke(header + "client canceled");
                    }
                }
                catch (OperationCanceledException)
                {
                    if (_Token.IsCancellationRequested)
                    {
                        Logger?.Invoke(header + "canceled");
                    }
                    else if (token.IsCancellationRequested)
                    {
                        Logger?.Invoke(header + "message send canceled");
                    }
                    else if (md.TokenSource.Token.IsCancellationRequested)
                    {
                        Logger?.Invoke(header + "client canceled");
                    }
                }
                catch (ObjectDisposedException)
                {
                    Logger?.Invoke(header + "disposed");
                }
                catch (WebSocketException)
                {
                    Logger?.Invoke(header + "websocket disconnected");
                }
                catch (SocketException)
                {
                    Logger?.Invoke(header + "socket disconnected");
                }
                catch (InvalidOperationException)
                {
                    Logger?.Invoke(header + "disconnected due to invalid operation");
                }
                catch (IOException)
                {
                    Logger?.Invoke(header + "IO disconnected");
                }
                catch (Exception e)
                {
                    Logger?.Invoke(header + "exception: " + Environment.NewLine + e.ToString());
                }
                finally
                {
                    md = null;
                    tokens = null;
                }
            }

            return false;
        }
         
        #endregion
    }
}
