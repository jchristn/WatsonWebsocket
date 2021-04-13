using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Net.NetworkInformation;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace WatsonWebsocket
{
    /// <summary>
    /// Watson websocket client.
    /// </summary>
    public class WatsonWsClient : IDisposable
    {
        #region Public-Members

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
        /// Indicates whether or not the client is connected to the server.
        /// </summary>
        public bool Connected
        {
            get
            {
                if (_ClientWs != null)
                {
                    if (_ClientWs.State == WebSocketState.Open) return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Enable or disable statistics.
        /// </summary>
        public bool EnableStatistics { get; set; } = true;

        /// <summary>
        /// Show Disconnect event while try to Connect.
        /// </summary>
        public bool DisconnectEventConnecting { get; set; } = false;

        /// <summary>
        /// Set KeepAlive to Connection Options.
        /// </summary>
        public int KeepAliveInterval
        {
            get
            {
                return _KeepAliveIntervalSeconds;
            }
            set
            {
                if (value < 1) throw new ArgumentException("ConnectTimeoutSeconds must be greater than zero.");
                _KeepAliveIntervalSeconds = value;
            }
        }

        /// <summary>
        /// Set Timeout to Start.
        /// </summary>
        public int ConnectTimeoutSeconds
        {
            get
            {
                return _ConnectTimeoutSeconds;
            }
            set
            {
                if (value < 1) throw new ArgumentException("ConnectTimeoutSeconds must be greater than zero.");
                _ConnectTimeoutSeconds = value;
            }
        }
        /// <summary>
        /// Event fired when a message is received.
        /// Parameter 1: byte array containing the data.
        /// </summary>
        public event EventHandler<MessageReceivedEventArgs> MessageReceived;

        /// <summary>
        /// Event fired when the client connects successfully to the server. 
        /// </summary>
        public event EventHandler ServerConnected;

        /// <summary>
        /// Event fired when the client disconnects from the server.
        /// </summary>
        public event EventHandler ServerDisconnected;

        /// <summary>
        /// Reset Subscriptions of disconnect event.
        /// </summary>
        public void DisconnectResetSubscriptions() => ServerDisconnected = delegate { };

        /// <summary>
        /// Method to invoke when sending a log message.
        /// </summary>
        public Action<string> Logger = null;

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

        private string _Header = "[WatsonWsClient] ";
        private bool _AcceptInvalidCertificates = true;
        private Uri _ServerUri;
        private string _ServerIp;
        private int _ServerPort;
        private string _ServerIpPort;
        private string _Url;
        private int _KeepAliveIntervalSeconds = 30;
        private int _ConnectTimeoutSeconds = 30;
        private ClientWebSocket _ClientWs;
        private CookieContainer _Cookies = new CookieContainer();
        private Action<ClientWebSocketOptions> _PreConfigureOptions;
        private event EventHandler<MessageReceivedEventArgs> InternalMessageReceived;
        private event EventHandler InternalServerDisconnected;
        private readonly SemaphoreSlim _SendLock = new SemaphoreSlim(1);
        private readonly SemaphoreSlim _InternalLock = new SemaphoreSlim(1);
        private CancellationTokenSource _TokenSource = new CancellationTokenSource();
        private CancellationToken _Token;
        private Statistics _Stats = new Statistics();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes the Watson websocket client.
        /// Be sure to call 'Start()' to start the client and connect to the server.
        /// </summary>
        /// <param name="serverIp">IP address of the server.</param>
        /// <param name="serverPort">TCP port of the server.</param>
        /// <param name="ssl">Enable or disable SSL.</param>
        public WatsonWsClient(
            string serverIp,
            int serverPort,
            bool ssl)
        {
            if (String.IsNullOrEmpty(serverIp)) throw new ArgumentNullException(nameof(serverIp));
            if (!Validator.IsHost(serverIp)) throw new ArgumentException("This element not is the host", nameof(serverIp));
            if (serverPort < 1) throw new ArgumentOutOfRangeException(nameof(serverPort));
            if (!Validator.IsPort(serverPort)) throw new ArgumentException("This element not is the port", nameof(serverPort));

            _ServerIp = serverIp;
            _ServerPort = serverPort;
            _ServerIpPort = serverIp + ":" + serverPort;

            if (ssl) _Url = "wss://" + _ServerIp + ":" + _ServerPort;
            else _Url = "ws://" + _ServerIp + ":" + _ServerPort;
            _ServerUri = new Uri(_Url);
            _Token = _TokenSource.Token;

            _ClientWs = new ClientWebSocket();
        }

        /// <summary>
        /// Initializes the Watson websocket client.
        /// Be sure to call 'Start()' to start the client and connect to the server.
        /// </summary>
        /// <param name="uri">The URI of the server endpoint.</param> 
        public WatsonWsClient(Uri uri)
        {
            _ServerUri = uri;
            _ServerIp = uri.Host;
            _ServerPort = uri.Port;
            _ServerIpPort = uri.Host + ":" + uri.Port;
            _Token = _TokenSource.Token;

            _ClientWs = new ClientWebSocket();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Tear down the client and dispose of background workers.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Allow pre configure Options WebSocket Client before Connect to Server.
        /// </summary>
        /// <returns>WebSocket Client.</returns>
        public WatsonWsClient ConfigureOptions(Action<ClientWebSocketOptions> options)
        {
            if (!Connected)
            {
                _PreConfigureOptions = options;
            }
            return this;
        }

        /// <summary>
        /// Allow Add Cookie to WebSocket Client before Connect to Server.
        /// </summary>
        public void AddCookie(Cookie cookie) => _Cookies.Add(cookie);

        /// <summary>
        /// Start the client and connect to the server.
        /// </summary>
        public void Start()
        {
            _Stats = new Statistics();
            if (_AcceptInvalidCertificates) ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;

            // Pre Configure WebSocket Client
            _ClientWs.Options.Cookies = _Cookies;
            _ClientWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(_KeepAliveIntervalSeconds);
            if (_PreConfigureOptions != null) _PreConfigureOptions(_ClientWs.Options);

            // Connect
            _ClientWs.ConnectAsync(_ServerUri, _Token).ContinueWith(AfterConnect);
        }

        /// <summary>
        /// Start the client and connect to the server.
        /// </summary>
        /// <returns>Task.</returns>
        public Task StartAsync()
        {
            _Stats = new Statistics();
            if (_AcceptInvalidCertificates) ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;

            // Pre Configure WebSocket Client
            _ClientWs.Options.Cookies = _Cookies;
            _ClientWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(_KeepAliveIntervalSeconds);
            if (_PreConfigureOptions != null) _PreConfigureOptions(_ClientWs.Options);

            // Connect
            return _ClientWs.ConnectAsync(_ServerUri, _Token).ContinueWith(AfterConnect);
        }

        /// <summary>
        /// Start the client and wait until to connect to the server.
        /// </summary>
        /// <param name="token">Cancellation token allowing for termination of this connecting.</param>
        /// <returns>Boolean indicating if the connection was successfully.</returns>
        public bool Start(CancellationToken token = default)
        {
            // Check Network is Connected
            if (!NetworkInterface.GetIsNetworkAvailable()) return false;

            _Stats = new Statistics();
            if (_AcceptInvalidCertificates) ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;

            Stopwatch sw = new Stopwatch();
            TimeSpan timeOut = TimeSpan.FromSeconds(_ConnectTimeoutSeconds);
            sw.Start();

            while (sw.Elapsed < timeOut)
            {
                if (token.IsCancellationRequested) break;
                // Reset ClientWebSocket (It dispose it-self)
                _ClientWs = new ClientWebSocket();

                // Pre Configure WebSocket Client
                _ClientWs.Options.Cookies = _Cookies;
                _ClientWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(_KeepAliveIntervalSeconds);
                if (_PreConfigureOptions != null) _PreConfigureOptions(_ClientWs.Options);

                // Try Connect
                try
                {
                    _ClientWs.ConnectAsync(_ServerUri, token).ContinueWith(AfterConnect).Wait();
                }
                catch (System.Net.WebSockets.WebSocketException) { }
                Thread.Sleep(500); // Wait to Reconnect or Change Connecting State

                // Check Connected
                if (_ClientWs.State == WebSocketState.Open)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Start the client and wait until to connect to the server.
        /// </summary>
        /// <param name="token">Cancellation token allowing for termination of this connecting.</param>
        /// <returns>Task with Boolean indicating if the connection was successfully.</returns>
        public async Task<bool> StartAsync(CancellationToken token = default)
        {
            // Check Network is Connected
            if (!NetworkInterface.GetIsNetworkAvailable()) return false;

            _Stats = new Statistics();
            if (_AcceptInvalidCertificates) ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;

            Stopwatch sw = new Stopwatch();
            TimeSpan timeOut = TimeSpan.FromSeconds(_ConnectTimeoutSeconds);
            sw.Start();

            while (sw.Elapsed < timeOut)
            {
                if (token.IsCancellationRequested) break;
                // Reset ClientWebSocket (It dispose it-self)
                _ClientWs = new ClientWebSocket();

                // Pre Configure WebSocket Client
                _ClientWs.Options.Cookies = _Cookies;
                _ClientWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(_KeepAliveIntervalSeconds);
                if (_PreConfigureOptions != null) _PreConfigureOptions(_ClientWs.Options);

                // Try Connect
                try
                {
                    await _ClientWs.ConnectAsync(_ServerUri, token).ContinueWith(AfterConnect);
                }
                catch (System.Net.WebSockets.WebSocketException) { }
                await Task.Delay(500); // Wait to Reconnect or Change Connecting State

                // Check Connected
                if (_ClientWs.State == WebSocketState.Open)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Close the client.
        /// </summary>
        public void Stop(bool forced = true)
        {
            // Reset Subscriptions for no generate DisconnectEvent in Forced Close the Client.
            if (forced) DisconnectResetSubscriptions();
            _ClientWs.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, _ClientWs.CloseStatusDescription, CancellationToken.None);
        }

        /// <summary>
        /// Close the client and wait to Disconnect.
        /// </summary>
        public async Task StopAsync(bool forced = true)
        {
            // Reset Subscriptions for no generate DisconnectEvent in Forced Close the Client.
            if (forced) DisconnectResetSubscriptions();
            await _ClientWs.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, _ClientWs.CloseStatusDescription, CancellationToken.None);
        }

        /// <summary>
        /// Send text data to the server asynchronously.
        /// </summary>
        /// <param name="data">String data.</param>
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string data, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            else return await MessageWriteAsync(Encoding.UTF8.GetBytes(data), WebSocketMessageType.Text, token);
        }

        /// <summary>
        /// Send binary data to the server asynchronously.
        /// </summary>
        /// <param name="data">Byte array containing data.</param>
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(byte[] data, CancellationToken token = default)
        {
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            return await MessageWriteAsync(data, WebSocketMessageType.Binary, token);
        }

        /// <summary>
        /// Send text data to the server asynchronously and wait for anything response.
        /// </summary>
        /// <param name="data">String data.</param>
        /// <param name="timeout">Maximum waiting time until one response (seconds).</param>
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<string> SendAndReplyAnythingAsync(string data, int timeout = 30, CancellationToken token = default)
        {
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            if (timeout < 1) throw new ArgumentException("Timeout invalid", nameof(data));
            string result = null;

            // Create Catcher Data
            var receivedEvent = new ManualResetEvent(false);
            await _InternalLock.WaitAsync(_Token);
            await Task.Run(() =>
            {
                InternalMessageReceived += (s, e) =>
                {
                    result = Encoding.UTF8.GetString(e.Data);
                    receivedEvent.Set();
                };
                // Detect Connection Lost
                InternalServerDisconnected += (s, e) => receivedEvent.Set();

                await MessageWriteAsync(Encoding.UTF8.GetBytes(data), WebSocketMessageType.Binary, token);
                receivedEvent.WaitOne(TimeSpan.FromSeconds(timeout));

                // Reset Internal Events
                InternalMessageReceived = delegate { };
                InternalServerDisconnected = delegate { };
                _InternalLock.Release();
            });

            return result;
        }

        /// <summary>
        /// Send text data to the server asynchronously and wait for text response.
        /// </summary>
        /// <param name="data">String data.</param>
        /// <param name="timeout">Maximum waiting time until one response (seconds).</param>
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<string> SendAndReplyAsync(string data, int timeout = 30, CancellationToken token = default)
        {
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            if (timeout < 1) throw new ArgumentException("Timeout must be zero or greater.", nameof(data));
            string result = null;

            // Create Catcher Data
            var receivedEvent = new ManualResetEvent(false);
            await _InternalLock.WaitAsync(_Token);
            await Task.Run(() =>
            {
                InternalMessageReceived += (s, e) =>
                {
                    if (e.MessageType == WebSocketMessageType.Text)
                    {
                        result = Encoding.UTF8.GetString(e.Data);
                    }
                    receivedEvent.Set();
                };
                // Detect Connection Lost
                InternalServerDisconnected += (s, e) => receivedEvent.Set();

                await MessageWriteAsync(Encoding.UTF8.GetBytes(data), WebSocketMessageType.Binary, token);
                receivedEvent.WaitOne(TimeSpan.FromSeconds(timeout));

                // Reset Internal Received
                InternalMessageReceived = delegate { };
                InternalServerDisconnected = delegate { };
                _InternalLock.Release();
            }, token);

            return result;
        }

        /// <summary>
        /// Send binary data to the server asynchronously and wait for anything response.
        /// </summary>
        /// <param name="data">Byte array containing data.</param>
        /// <param name="timeout">Maximum waiting time until one respons (seconds)e.</param>
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<byte[]> SendAndReplyAnythingAsync(byte[] data, int timeout = 30, CancellationToken token = default)
        {
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            if (timeout < 1) throw new ArgumentException("Timeout must be zero or greater.", nameof(data));
            byte[] result = null;

            // Create Catcher Data
            var receivedEvent = new ManualResetEvent(false);
            await _InternalLock.WaitAsync(_Token);
            await Task.Run(() =>
            {
                InternalMessageReceived += (s, e) =>
                {
                    result = e.Data;
                    receivedEvent.Set();
                };
                // Detect Connection Lost
                InternalServerDisconnected += (s, e) => receivedEvent.Set();

                await MessageWriteAsync(data, WebSocketMessageType.Binary, token);
                receivedEvent.WaitOne(TimeSpan.FromSeconds(timeout));

                // Reset Internal Received
                InternalMessageReceived = delegate { };
                InternalServerDisconnected = delegate { };
                _InternalLock.Release();
            });

            return result;
        }

        /// <summary>
        /// Send binary data to the server asynchronously and wait for binary response.
        /// </summary>
        /// <param name="data">Byte array containing data.</param>
        /// <param name="timeout">Maximum waiting time until one response (seconds).</param>
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<byte[]> SendAndReplyAsync(byte[] data, int timeout = 30, CancellationToken token = default)
        {
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            if (timeout < 1) throw new ArgumentException("Timeout must be zero or greater.", nameof(data));
            byte[] result = null;

            // Create Catcher Data
            var receivedEvent = new ManualResetEvent(false);
            await _InternalLock.WaitAsync(_Token);
            await Task.Run(() =>
            {
                InternalMessageReceived += (s, e) =>
                {
                    if (e.MessageType == WebSocketMessageType.Binary)
                    {
                        result = e.Data;
                    }
                    receivedEvent.Set();
                };
                // Detect Connection Lost
                InternalServerDisconnected += (s, e) => receivedEvent.Set();

                await MessageWriteAsync(data, WebSocketMessageType.Binary, token);
                receivedEvent.WaitOne(TimeSpan.FromSeconds(timeout));

                // Reset Internal Received
                InternalMessageReceived = delegate { };
                InternalServerDisconnected = delegate { };

                _InternalLock.Release();
            });

            return result;
        }

        /// <summary>
        /// Send binary data to the server asynchronously.
        /// </summary>
        /// <param name="data">Byte array containing data.</param>
        /// <param name="msgType">Web socket message type.</param>
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(byte[] data, WebSocketMessageType msgType, CancellationToken token = default)
        {
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            return await MessageWriteAsync(data, msgType, token);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Tear down the client and dispose of background workers.
        /// </summary>
        /// <param name="disposing">Disposing.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_ClientWs != null)
                {
                    // see https://mcguirev10.com/2019/08/17/how-to-close-websocket-correctly.html  

                    if (_ClientWs.State == WebSocketState.Open)
                    {
                        Stop();
                        _ClientWs.Dispose();
                    }
                }

                _TokenSource.Cancel();

                Logger?.Invoke(_Header + "dispose complete");
            }
        }

        private void AfterConnect(Task task)
        {
            if (task.IsCompleted)
            {
                if (_ClientWs.State == WebSocketState.Open)
                {
                    Task.Run(() =>
                    {
                        Task.Run(() => DataReceiver(), _Token);
                        ServerConnected?.Invoke(this, EventArgs.Empty);
                    }, _Token);
                }
                else
                {
                    if (DisconnectEventConnecting)
                    {
                        ServerDisconnected?.Invoke(this, EventArgs.Empty);
                        InternalServerDisconnected?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
            else
            {
                if (DisconnectEventConnecting)
                {
                    ServerDisconnected?.Invoke(this, EventArgs.Empty);
                    InternalServerDisconnected?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private async Task DataReceiver()
        {
            try
            {
                while (true)
                {
                    if (_Token.IsCancellationRequested) break;
                    MessageReceivedEventArgs msg = await MessageReadAsync();

                    if (msg != null)
                    {
                        if (EnableStatistics)
                        {
                            _Stats.IncrementReceivedMessages();
                            _Stats.AddReceivedBytes(msg.Data.Length);
                        }

                        Task internal_unawaited = Task.Run(() => InternalMessageReceived?.Invoke(this, msg), _Token);
                        if (msg.MessageType != WebSocketMessageType.Close)
                        {
                            Task unawaited = Task.Run(() => MessageReceived?.Invoke(this, msg), _Token);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Logger?.Invoke(_Header + "data receiver canceled");
            }
            catch (WebSocketException)
            {
                Logger?.Invoke(_Header + "websocket disconnected");
            }
            catch (Exception e)
            {
                Logger?.Invoke(_Header + "exception: " + Environment.NewLine + e.ToString());
            }

            ServerDisconnected?.Invoke(this, EventArgs.Empty);
            InternalServerDisconnected?.Invoke(this, EventArgs.Empty);
        }

        private async Task<MessageReceivedEventArgs> MessageReadAsync()
        {
            // Do not catch exceptions, let them get caught by the data reader to destroy the connection

            if (_ClientWs == null) return null;
            byte[] buffer = new byte[65536];
            byte[] data = null;

            WebSocketReceiveResult result = null;

            using (MemoryStream dataMs = new MemoryStream())
            {
                buffer = new byte[buffer.Length];
                ArraySegment<byte> bufferSegment = new ArraySegment<byte>(buffer);

                if (_ClientWs.State == WebSocketState.CloseReceived
                    || _ClientWs.State == WebSocketState.Closed)
                {
                    throw new WebSocketException("Websocket close received");
                }

                while (_ClientWs.State == WebSocketState.Open)
                {
                    result = await _ClientWs.ReceiveAsync(bufferSegment, _Token);
                    if (result.Count > 0)
                    {
                        await dataMs.WriteAsync(buffer, 0, result.Count);
                    }

                    if (result.EndOfMessage)
                    {
                        data = dataMs.ToArray();
                        break;
                    }
                }
            }

            return new MessageReceivedEventArgs(_ServerIpPort, data, result.MessageType);
        }

        private async Task<bool> MessageWriteAsync(byte[] data, WebSocketMessageType msgType, CancellationToken token)
        {
            bool disconnectDetected = false;

            using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_Token, token))
            {
                try
                {
                    if (_ClientWs == null || _ClientWs.State != WebSocketState.Open)
                    {
                        Logger?.Invoke(_Header + "not connected");
                        disconnectDetected = true;
                        return false;
                    }

                    await _SendLock.WaitAsync(_Token);

                    try
                    {
                        await _ClientWs.SendAsync(new ArraySegment<byte>(data, 0, data.Length), msgType, true, token);
                    }
                    catch { }
                    finally
                    {
                        _SendLock.Release();
                    }

                    if (EnableStatistics)
                    {
                        _Stats.IncrementSentMessages();
                        _Stats.AddSentBytes(data.Length);
                    }

                    return true;
                }
                catch (TaskCanceledException)
                {
                    if (_Token.IsCancellationRequested)
                    {
                        Logger?.Invoke(_Header + "canceled");
                        disconnectDetected = true;
                    }
                    else if (token.IsCancellationRequested)
                    {
                        Logger?.Invoke(_Header + "message send canceled");
                    }

                    return false;
                }
                catch (OperationCanceledException)
                {
                    if (_Token.IsCancellationRequested)
                    {
                        Logger?.Invoke(_Header + "canceled");
                        disconnectDetected = true;
                    }
                    else if (token.IsCancellationRequested)
                    {
                        Logger?.Invoke(_Header + "message send canceled");
                    }

                    return false;
                }
                catch (WebSocketException)
                {
                    Logger?.Invoke(_Header + "websocket disconnected");
                    disconnectDetected = true;
                    return false;
                }
                catch (ObjectDisposedException)
                {
                    Logger?.Invoke(_Header + "disposed");
                    disconnectDetected = true;
                    return false;
                }
                catch (SocketException)
                {
                    Logger?.Invoke(_Header + "socket disconnected");
                    disconnectDetected = true;
                    return false;
                }
                catch (InvalidOperationException)
                {
                    Logger?.Invoke(_Header + "disconnected due to invalid operation");
                    disconnectDetected = true;
                    return false;
                }
                catch (IOException)
                {
                    Logger?.Invoke(_Header + "IO disconnected");
                    disconnectDetected = true;
                    return false;
                }
                catch (Exception e)
                {
                    Logger?.Invoke(_Header + "exception: " + Environment.NewLine + e.ToString());
                    disconnectDetected = true;
                    return false;
                }
                finally
                {
                    if (disconnectDetected)
                    {
                        Dispose();
                        ServerDisconnected?.Invoke(this, EventArgs.Empty);
                        InternalServerDisconnected?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }

        #endregion
    }
}
