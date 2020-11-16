using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
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
        private ClientWebSocket _ClientWs; 
        private readonly SemaphoreSlim _SendLock = new SemaphoreSlim(1);
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
        /// Start the client and connect to the server.
        /// </summary>
        public void Start()
        {
            _Stats = new Statistics();
            if (_AcceptInvalidCertificates) ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;   
            _ClientWs.ConnectAsync(_ServerUri, _Token).ContinueWith(AfterConnect);
        }

        /// <summary>
        /// Close the client.
        /// </summary>
        public void Stop()
        {
            _ClientWs.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait();
        }

        /// <summary>
        /// Send text data to the server asynchronously.
        /// </summary>
        /// <param name="data">String data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string data)
        {
            if (String.IsNullOrEmpty(data)) throw new ArgumentNullException(nameof(data));
            else return await MessageWriteAsync(Encoding.UTF8.GetBytes(data), WebSocketMessageType.Text);
        }

        /// <summary>
        /// Send binary data to the server asynchronously.
        /// </summary>
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(byte[] data)
        {
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            return await MessageWriteAsync(data, WebSocketMessageType.Binary);
        }

        /// <summary>
        /// Send binary data to the server asynchronously.
        /// </summary>
        /// <param name="data">Byte array containing data.</param>
        /// <param name="msgType">Web socket message type.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(byte[] data, WebSocketMessageType msgType)
        {
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            return await MessageWriteAsync(data, msgType);
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
                    try
                    {
                        // see https://mcguirev10.com/2019/08/17/how-to-close-websocket-correctly.html  

                        if (_ClientWs.State == WebSocketState.Open)
                        { 
                            _ClientWs.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                            _ClientWs.Dispose();
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
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
                    ServerDisconnected?.Invoke(this, EventArgs.Empty);
                }
            }
            else
            {  
                ServerDisconnected?.Invoke(this, EventArgs.Empty);
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
                        _Stats.IncrementReceivedMessages();
                        _Stats.AddReceivedBytes(msg.Data.Length);

                        Task unawaited = Task.Run(() => MessageReceived?.Invoke(this, msg), _Token);
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
         
        private async Task<bool> MessageWriteAsync(byte[] data, WebSocketMessageType msgType)
        { 
            bool disconnectDetected = false;

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
                    await _ClientWs.SendAsync(new ArraySegment<byte>(data, 0, data.Length), msgType, true, CancellationToken.None);
                } 
                finally
                {
                    _SendLock.Release();
                }

                _Stats.IncrementSentMessages();
                _Stats.AddSentBytes(data.Length);

                return true; 
            }
            catch (OperationCanceledException)
            {
                Logger?.Invoke(_Header + "canceled");
                disconnectDetected = true;
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

        #endregion
    }
}
