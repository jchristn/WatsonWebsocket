﻿using System;
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
        public event EventHandler<ConnectionEventArgs> ClientConnected;

        /// <summary>
        /// Event fired when a client disconnects.
        /// </summary>
        public event EventHandler<DisconnectionEventArgs> ClientDisconnected;

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
        /// Listener prefixes.
        /// </summary>
        public List<string> ListenerPrefixes
        {
            get
            {
                return _ListenerPrefixes;
            }
        }

        /// <summary>
        /// Method to invoke when sending a log message.
        /// </summary>
        public Action<string> Logger = null;

        /// <summary>
        /// Method to invoke when receiving a raw (non-websocket) HTTP request.
        /// </summary>
        public Action<HttpListenerContext> HttpHandler = null;

        /// <summary>
        /// Header used by client to indicate which GUID should be assigned.
        /// </summary>
        public string GuidHeader
        {
            get
            {
                return _GuidHeader;
            }
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(GuidHeader));
                _GuidHeader = value;
            }
        }

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
        private ConcurrentDictionary<Guid, ClientMetadata> _Clients = new ConcurrentDictionary<Guid, ClientMetadata>();
        private CancellationTokenSource _TokenSource = new CancellationTokenSource();
        private CancellationToken _Token; 
        private Task _AcceptConnectionsTask;
        private Statistics _Stats = new Statistics();
        private string _GuidHeader = "x-guid";

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
                _Listener.Prefixes.Add(prefix);

            _Token = _TokenSource.Token;
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

            _Token = _TokenSource.Token;
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
                    host = dnsLookup.AddressList.First().ToString();
                else
                    throw new ArgumentException("Cannot resolve address to IP.");
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
                _Listener.Prefixes.Add(prefix);

            _Token = _TokenSource.Token;
        }

        /// <summary>
        /// Initializes the Watson websocket server.  
        /// Be sure to call 'Start()' to start the server.
        /// </summary>
        /// <param name="settings">Websocket settings.</param>
        public WatsonWsServer(WebsocketSettings settings) : this(settings?.Hostnames, settings?.Port ?? 0, settings?.Ssl ?? false)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
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
        /// Start the server and begin accepting new connections.
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
        /// Start the server and begin accepting new connections.
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
        /// Stop the server and disconnect each connection. 
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
        /// <param name="guid">Globally-unique identifier of the recipient client.</param>
        /// <param name="data">String containing data.</param>
        /// <param name="msgType">Web socket message type.</param>
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public Task<bool> SendAsync(Guid guid, string data, WebSocketMessageType msgType = WebSocketMessageType.Text, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(data)) data = "";
            return SendAsync(guid, Encoding.UTF8.GetBytes(data), msgType, token);
        }

        /// <summary>
        /// Send binary data to the specified client, asynchronously.
        /// </summary>
        /// <param name="guid">Globally-unique identifier of the recipient client.</param>
        /// <param name="data">Byte array containing data.</param> 
        /// <param name="msgType">Web socket message type.</param>
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public Task<bool> SendAsync(Guid guid, byte[] data, WebSocketMessageType msgType = WebSocketMessageType.Binary, CancellationToken token = default)
        {
            if (data == null) data = new byte[0];
            return SendAsync(guid, new ArraySegment<byte>(data), msgType, token);
        }

        /// <summary>
        /// Send binary data to the specified client, asynchronously.
        /// </summary>
        /// <param name="guid">Globally-unique identifier of the recipient client.</param>
        /// <param name="data">ArraySegment containing data.</param> 
        /// <param name="msgType">Web socket message type.</param>
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public Task<bool> SendAsync(Guid guid, ArraySegment<byte> data, WebSocketMessageType msgType = WebSocketMessageType.Binary, CancellationToken token = default)
        {
            if (data.Array == null || data.Count < 1) throw new ArgumentNullException(nameof(data));
            if (!_Clients.TryGetValue(guid, out ClientMetadata client))
            {
                Logger?.Invoke(_Header + "unable to find client " + guid.ToString());
                return Task.FromResult(false);
            }

            Task<bool> task = MessageWriteAsync(client, data, msgType, token);
            client = null;
            return task;
        }

        /// <summary>
        /// Determine whether or not the specified client is connected to the server.
        /// </summary>
        /// <param name="guid">Globally-unique identifier of the recipient client.</param>
        /// <returns>Boolean indicating if the client is connected to the server.</returns>
        public bool IsClientConnected(Guid guid)
        {
            return _Clients.Any(c => c.Key.Equals(guid));
        }

        /// <summary>
        /// Retrieve list of connected clients.
        /// </summary>
        /// <returns>A list containing client metadata.</returns>
        public IEnumerable<ClientMetadata> ListClients()
        {
            return _Clients.Values;
        }

        /// <summary>
        /// Forcefully disconnect a client.
        /// </summary>
        /// <param name="guid">Globally-unique identifier of the recipient client.</param>
        public void DisconnectClient(Guid guid)
        { 
            if (_Clients.TryGetValue(guid, out var client))
            {
                lock (client)
                {
                    try
                    {
                        // Only attempt to close if it's in a state where closing is valid
                        if (client.Ws.State == WebSocketState.Open || client.Ws.State == WebSocketState.CloseReceived)
                        {
                            client.Ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", client.TokenSource.Token).Wait(1000);
                        }
                    }
                    catch (Exception)
                    {
                    }
                    finally
                    {
                        client.TokenSource.Cancel();
                        client.Ws.Dispose();
                    }
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
                    foreach (KeyValuePair<Guid, ClientMetadata> client in _Clients)
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
            bool exiting = false;

            while (!cancelToken.IsCancellationRequested)
            {
                try
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

                    await Task.Run(() =>
                    {
                        Logger?.Invoke(_Header + "starting data receiver for " + ipPort);

                        CancellationTokenSource tokenSource = new CancellationTokenSource();
                        CancellationToken token = tokenSource.Token;

                        Task.Run(async () =>
                        {
                            Guid guid = Guid.NewGuid();
                            string guidStr = ctx.Request.Headers.Get(_GuidHeader);
                            if (!String.IsNullOrEmpty(guidStr)) guid = Guid.Parse(guidStr);

                            WebSocketContext wsContext = await ctx.AcceptWebSocketAsync(subProtocol: null);
                            WebSocket ws = wsContext.WebSocket;
                            ClientMetadata md = new ClientMetadata(ctx, ws, wsContext, tokenSource, guid);
                             
                            _Clients.TryAdd(md.Guid, md);

                            ClientConnected?.Invoke(this, new ConnectionEventArgs(md, ctx.Request));
                            await Task.Run(() => DataReceiver(md), token);
                             
                        }, token);

                    }, _Token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    // thrown when disposed
                    exiting = true;
                    break;
                }
                catch (OperationCanceledException)
                {
                    // thrown when disposed
                    exiting = true;
                    break;
                }
                catch (ObjectDisposedException)
                {
                    // thrown when disposed
                    exiting = true;
                    break;
                }
                catch (HttpListenerException)
                {
                    // thrown when stopped
                    exiting = true;
                    break;
                }
                catch (Exception e)
                {
                    Logger?.Invoke(_Header + "listener exception:" + Environment.NewLine + e.ToString());
                }
                finally
                {
                    if (exiting)
                    {
                        Logger?.Invoke(_Header + "listener stopped");
                        ServerStopped?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }

        private async Task DataReceiver(ClientMetadata client)
        { 
            string header = "[WatsonWsServer " + client.Guid.ToString() + "] ";
            Logger?.Invoke(header + "starting data receiver");
            byte[] buffer = new byte[65536];

            try
            { 
                while (true)
                {
                    MessageReceivedEventArgs msg = await MessageReadAsync(client, buffer).ConfigureAwait(false);

                    if (msg != null)
                    {
                        if (EnableStatistics)
                        {
                            _Stats.IncrementReceivedMessages();
                            _Stats.AddReceivedBytes(msg.Data.Count);
                        }

                        if (msg.Data != null)
                        {
                            Task unawaited = Task.Run(() => MessageReceived?.Invoke(this, msg), client.TokenSource.Token);
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
                _Clients.TryRemove(client.Guid, out _);
                string ipPort = client.IpPort;
                ClientDisconnected?.Invoke(this, new DisconnectionEventArgs(client));
                client.Ws.Dispose();
                Logger?.Invoke(header + "disconnected");
            }
        }
         
        private async Task<MessageReceivedEventArgs> MessageReadAsync(ClientMetadata client, byte[] buffer)
        {
            string header = "[WatsonWsServer " + client.Guid.ToString() + "] ";

            using (MemoryStream ms = new MemoryStream())
            {
                ArraySegment<byte> seg = new ArraySegment<byte>(buffer);

                while (true)
                {
                    WebSocketReceiveResult result = await client.Ws.ReceiveAsync(seg, client.TokenSource.Token).ConfigureAwait(false);
                    if (result.CloseStatus != null)
                    {
                        Logger?.Invoke(header + "close received");
                        await client.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        throw new WebSocketException("Websocket closed.");
                    }

                    if (client.Ws.State != WebSocketState.Open)
                    {
                        Logger?.Invoke(header + "websocket no longer open");
                        throw new WebSocketException("Websocket closed.");
                    }

                    if (client.TokenSource.Token.IsCancellationRequested)
                    {
                        Logger?.Invoke(header + "cancel requested");
                    }

                    if (result.Count > 0)
                    {
                        ms.Write(buffer, 0, result.Count);
                    }

                    if (result.EndOfMessage)
                    {
                        return new MessageReceivedEventArgs(client, new ArraySegment<byte>(ms.GetBuffer(), 0, (int)ms.Length), result.MessageType);
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
