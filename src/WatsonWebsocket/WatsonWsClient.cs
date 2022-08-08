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
        /// Event fired when a message is received.
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
        private Uri _ServerUri = null;
        private string _ServerIp = null;
        private int _ServerPort = 0;
        private string _ServerIpPort = null;
        private string _Url = null;
        private int _KeepAliveIntervalSeconds = 30;
        private ClientWebSocket _ClientWs = null;
        private CookieContainer _Cookies = new CookieContainer();
        private Action<ClientWebSocketOptions> _PreConfigureOptions;

        private event EventHandler<MessageReceivedEventArgs> _AwaitingSyncResponseEvent;

        private readonly SemaphoreSlim _SendLock = new SemaphoreSlim(1);
        private readonly SemaphoreSlim _AwaitingSyncResposeLock = new SemaphoreSlim(1);

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
            if (serverPort < 1) throw new ArgumentOutOfRangeException(nameof(serverPort));

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
        /// Pre-configure websocket client options prior to connecting to the server.
        /// </summary>
        /// <returns>WatsonWsClient.</returns>
        public WatsonWsClient ConfigureOptions(Action<ClientWebSocketOptions> options)
        {
            if (!Connected)
            {
                _PreConfigureOptions = options;
            }

            return this;
        }

        /// <summary>
        /// Add a cookie prior to connecting to the server.
        /// </summary>
        public void AddCookie(Cookie cookie) => _Cookies.Add(cookie);

        /// <summary>
        /// Start the client and connect to the server.
        /// </summary>
        public void Start()
        {
            _Stats = new Statistics();
            if (_AcceptInvalidCertificates) SetInvalidCertificateAcceptance();

            _ClientWs.Options.Cookies = _Cookies;
            _ClientWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(_KeepAliveIntervalSeconds);
            
            if (_PreConfigureOptions != null)
            {
                _PreConfigureOptions(_ClientWs.Options);
            }

            _ClientWs.ConnectAsync(_ServerUri, _Token).ContinueWith(AfterConnect).Wait();
        }

        /// <summary>
        /// Start the client and connect to the server.
        /// </summary>
        /// <returns>Task.</returns>
        public Task StartAsync()
        {
            _Stats = new Statistics();
            if (_AcceptInvalidCertificates) SetInvalidCertificateAcceptance();

            _ClientWs.Options.Cookies = _Cookies;
            _ClientWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(_KeepAliveIntervalSeconds);

            if (_PreConfigureOptions != null)
            {
                _PreConfigureOptions(_ClientWs.Options);
            }

            // Connect
            return _ClientWs.ConnectAsync(_ServerUri, _Token).ContinueWith(AfterConnect);
        }

        /// <summary>
        /// Start the client and attempt to connect to the server until the timeout is reached.
        /// </summary>
        /// <param name="timeout">Timeout in seconds.</param>
        /// <param name="token">Cancellation token to terminate connection attempts.</param>
        /// <returns>Boolean indicating if the connection was successful.</returns>
        public bool StartWithTimeout(int timeout = 30, CancellationToken token = default)
        {
            if (timeout < 1) throw new ArgumentException("Timeout must be greater than zero seconds.");

            _Stats = new Statistics();
            if (_AcceptInvalidCertificates) SetInvalidCertificateAcceptance();

            Stopwatch sw = new Stopwatch();
            TimeSpan timeOut = TimeSpan.FromSeconds(timeout);
            sw.Start();

            try
            {
                while (sw.Elapsed < timeOut)
                {
                    if (token.IsCancellationRequested) break;
                    _ClientWs = new ClientWebSocket();

                    _ClientWs.Options.Cookies = _Cookies;
                    _ClientWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(_KeepAliveIntervalSeconds);
                    if (_PreConfigureOptions != null)
                    {
                        _PreConfigureOptions(_ClientWs.Options);
                    }

                    try
                    {
                        _ClientWs.ConnectAsync(_ServerUri, token).ContinueWith(AfterConnect).Wait();
                    }
                    catch (TaskCanceledException)
                    {
                        return false;
                    }
                    catch (OperationCanceledException)
                    {
                        return false;
                    }
                    catch (WebSocketException)
                    {
                        // do nothing, continue
                    }

                    Task.Delay(100).Wait();

                    // Check if connected
                    if (_ClientWs.State == WebSocketState.Open)
                    {
                        return true;
                    }
                }
            }
            catch (TaskCanceledException)
            {

            }
            catch (OperationCanceledException)
            {

            }

            return false;
        }

        /// <summary>
        /// Start the client and attempt to connect to the server until the timeout is reached.
        /// </summary>
        /// <param name="timeout">Timeout in seconds.</param>
        /// <param name="token">Cancellation token to terminate connection attempts.</param>
        /// <returns>Task returning Boolean indicating if the connection was successful.</returns>
        public async Task<bool> StartWithTimeoutAsync(int timeout = 30, CancellationToken token = default)
        {
            if (timeout < 1) throw new ArgumentException("Timeout must be greater than zero seconds.");

            _Stats = new Statistics();
            if (_AcceptInvalidCertificates) SetInvalidCertificateAcceptance();

            Stopwatch sw = new Stopwatch();
            TimeSpan timeOut = TimeSpan.FromSeconds(timeout);
            sw.Start();

            try
            {
                while (sw.Elapsed < timeOut)
                {
                    if (token.IsCancellationRequested) break;
                    _ClientWs = new ClientWebSocket();

                    _ClientWs.Options.Cookies = _Cookies;
                    _ClientWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(_KeepAliveIntervalSeconds);
                    if (_PreConfigureOptions != null)
                    {
                        _PreConfigureOptions(_ClientWs.Options);
                    }

                    try
                    {
                        await _ClientWs.ConnectAsync(_ServerUri, token).ContinueWith(AfterConnect);
                    }
                    catch (TaskCanceledException)
                    {
                        return false;
                    }
                    catch (OperationCanceledException)
                    {
                        return false;
                    }
                    catch (WebSocketException)
                    {
                        // do nothing
                    }

                    await Task.Delay(100);

                    // Check if connected
                    if (_ClientWs.State == WebSocketState.Open)
                    {
                        return true;
                    }
                }
            }
            catch (TaskCanceledException)
            {

            }
            catch (OperationCanceledException)
            {

            }

            return false;
        }

        /// <summary>
        /// Disconnect the client.
        /// </summary>
        public void Stop()
        {
            Stop(WebSocketCloseStatus.NormalClosure, _ClientWs.CloseStatusDescription);
        }

        /// <summary>
        /// Disconnect the client.
        /// </summary>
        public async Task StopAsync()
        {
            await StopAsync(WebSocketCloseStatus.NormalClosure, _ClientWs.CloseStatusDescription);
        }

        /// <summary>
        /// Disconnect the client by code and reason.
        /// </summary>
        /// <param name="closeCode">Close code.</param>
        /// <param name="reason">Close by reason.</param>
        public void Stop(WebSocketCloseStatus closeCode, string reason)
        { 
            _ClientWs.CloseOutputAsync(closeCode, reason, _Token).Wait();
        } 
        
        /// <summary>
        /// Disconnect the client by code and reason.
        /// </summary>
        /// <param name="closeCode">Close code.</param>
        /// <param name="reason">Close by reason.</param>
        public async Task StopAsync(WebSocketCloseStatus closeCode, string reason)
        { 
            await _ClientWs.CloseOutputAsync(closeCode, reason, _Token).ConfigureAwait(false);
        } 

        /// <summary>
        /// Send text data to the server asynchronously.
        /// </summary>
        /// <param name="data">String data.</param>
        /// <param name="msgType">Message type.</param>
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string data, WebSocketMessageType msgType = WebSocketMessageType.Text, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            else return await MessageWriteAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(data)), msgType, token);
        }

        /// <summary>
        /// Send binary data to the server asynchronously.
        /// </summary>
        /// <param name="data">Byte array containing data.</param>
        /// <param name="msgType">Message type.</param>
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(byte[] data, WebSocketMessageType msgType = WebSocketMessageType.Binary, CancellationToken token = default)
        {
            return await MessageWriteAsync(new ArraySegment<byte>(data), msgType, token);
        }

        /// <summary>
        /// Send binary data to the server asynchronously.
        /// </summary>
        /// <param name="data">ArraySegment containing data.</param>
        /// <param name="msgType">Message type.</param>
        /// <param name="token">Cancellation token allowing for termination of this request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(ArraySegment<byte> data, WebSocketMessageType msgType = WebSocketMessageType.Binary, CancellationToken token = default)
        {
            if (data.Array == null || data.Count < 1) throw new ArgumentNullException(nameof(data));
            return await MessageWriteAsync(data, msgType, token);
        }

        /// <summary>
        /// Send text data to the server and wait for a response.
        /// </summary>
        /// <param name="data">String data.</param>
        /// <param name="timeout">Timeout, in seconds.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>String from response.</returns>
        public async Task<string> SendAndWaitAsync(string data, int timeout = 30, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            if (timeout < 1) throw new ArgumentException("Timeout must be greater than zero seconds.", nameof(data));
            string result = null;

            var receivedEvent = new ManualResetEvent(false);
            await _AwaitingSyncResposeLock.WaitAsync(_Token);

            await Task.Run(async () =>
            {
                _AwaitingSyncResponseEvent += (s, e) =>
                {
                    result = Encoding.UTF8.GetString(e.Data.Array, 0, e.Data.Count);
                    receivedEvent.Set();
                };

                await MessageWriteAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(data)), WebSocketMessageType.Text, token);
                receivedEvent.WaitOne(TimeSpan.FromSeconds(timeout));

                _AwaitingSyncResponseEvent = null;
                _AwaitingSyncResposeLock.Release();
            });

            return result;
        }

        /// <summary>
        /// Send binary data to the server and wait for a response.
        /// </summary>
        /// <param name="data">Byte array containing data.</param>
        /// <param name="timeout">Timeout, in seconds.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Byte array from response.</returns>
        public async Task<ArraySegment<byte>> SendAndWaitAsync(byte[] data, int timeout = 30, CancellationToken token = default)
        {
            return await SendAndWaitAsync(new ArraySegment<byte>(data), timeout, token);
        }

        /// <summary>
        /// Send binary data to the server and wait for a response.
        /// </summary>
        /// <param name="data">ArraySegment containing data.</param>
        /// <param name="timeout">Timeout, in seconds.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Byte array from response.</returns>
        public async Task<ArraySegment<byte>> SendAndWaitAsync(ArraySegment<byte> data, int timeout = 30, CancellationToken token = default)
        {
            if (data.Array == null || data.Count < 1) throw new ArgumentNullException(nameof(data));
            if (timeout < 1) throw new ArgumentException("Timeout must be zero or greater.", nameof(data));
            ArraySegment<byte> result = default;

            var receivedEvent = new ManualResetEvent(false);
            await _AwaitingSyncResposeLock.WaitAsync(_Token);

            await Task.Run(async () =>
            {
                _AwaitingSyncResponseEvent += (s, e) =>
                {
                    result = e.Data;
                    receivedEvent.Set();
                };

                await MessageWriteAsync(data, WebSocketMessageType.Binary, token);
                receivedEvent.WaitOne(TimeSpan.FromSeconds(timeout));

                _AwaitingSyncResponseEvent = null;
                _AwaitingSyncResposeLock.Release();
            });

            return result;
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

        private void SetInvalidCertificateAcceptance()
        {
#if NETFRAMEWORK
            ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;
#endif

#if NET || NETSTANDARD || NETCOREAPP
            _ClientWs.Options.RemoteCertificateValidationCallback +=
                (message, certificate, chain, sslPolicyErrors) =>
                {
                    return true;
                };
#endif
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
            }
        }

        private async Task DataReceiver()
        {
            byte[] buffer = new byte[65536];

            try
            {
                while (true)
                {
                    if (_Token.IsCancellationRequested) break;
                    MessageReceivedEventArgs msg = await MessageReadAsync(buffer);

                    if (msg != null)
                    {
                        if (EnableStatistics)
                        {
                            _Stats.IncrementReceivedMessages();
                            _Stats.AddReceivedBytes(msg.Data.Count);
                        }

                        if (msg.MessageType != WebSocketMessageType.Close)
                        {
                            if (_AwaitingSyncResponseEvent != null)
                            {
                                _AwaitingSyncResponseEvent?.Invoke(this, msg);
                            }
                            else
                            {
                                MessageReceived?.Invoke(this, msg);
                            }
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
        }
        private async Task<MessageReceivedEventArgs> MessageReadAsync(byte[] buffer)
        {
            // Do not catch exceptions, let them get caught by the data reader to destroy the connection

            if (_ClientWs == null) return null;
            ArraySegment<byte> data = default;

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
                        data = new ArraySegment<byte>(dataMs.GetBuffer(), 0, (int)dataMs.Length);
                        break;
                    }
                }
            }

            return new MessageReceivedEventArgs(_ServerIpPort, data, result.MessageType);
        }

        private async Task<bool> MessageWriteAsync(ArraySegment<byte> data, WebSocketMessageType msgType, CancellationToken token)
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

                    await _SendLock.WaitAsync(_Token).ConfigureAwait(false);

                    try
                    {
                        await _ClientWs.SendAsync(data, msgType, true, token).ConfigureAwait(false);
                    }
                    catch 
                    { 
                    
                    }
                    finally
                    {
                        _SendLock.Release();
                    }

                    if (EnableStatistics)
                    {
                        _Stats.IncrementSentMessages();
                        _Stats.AddSentBytes(data.Count);
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
                    }
                }
            }
        }

        #endregion
    }
}
