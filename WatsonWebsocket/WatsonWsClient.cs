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
        /// Enable or disable console debugging.
        /// </summary>
        public bool Debug = false;

        /// <summary>
        /// Indicates whether or not the client is connected to the server.
        /// </summary>
        public bool Connected
        {
            get
            {
                return _Connected;
            }
        }

        /// <summary>
        /// Callback called when a message is received.
        /// Parameter 1: byte array containing the data.
        /// </summary>
        public Func<byte[], Task> MessageReceived = null;

        /// <summary>
        /// Callback called when the client connects successfully to the server. 
        /// </summary>
        public Func<Task> ServerConnected = null;

        /// <summary>
        /// Callback called when the client disconnects from the server.
        /// </summary>
        public Func<Task> ServerDisconnected = null;

        #endregion

        #region Private-Members

        private bool _AcceptInvalidCertificates = true;
        private Uri _ServerUri;
        private string _ServerIp;
        private int _ServerPort;
        private string _Url;
        private ClientWebSocket _ClientWs;
        private bool _Connected = false; 

        private readonly SemaphoreSlim SendLock = new SemaphoreSlim(1);
        private CancellationTokenSource TokenSource = new CancellationTokenSource();
        private CancellationToken Token;

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

            if (ssl) _Url = "wss://" + _ServerIp + ":" + _ServerPort;
            else _Url = "ws://" + _ServerIp + ":" + _ServerPort;
            _ServerUri = new Uri(_Url); 
              
            Token = TokenSource.Token;

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
            Token = TokenSource.Token;

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
        }

        /// <summary>
        /// Start the client and connect to the server.
        /// </summary>
        public void Start()
        {
            if (_AcceptInvalidCertificates) ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;

            _ClientWs.ConnectAsync(_ServerUri, CancellationToken.None)
                .ContinueWith(AfterConnect);
        }

        /// <summary>
        /// Send data to the server asynchronously
        /// </summary>
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(byte[] data)
        {
            return await MessageWriteAsync(data);
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
                TokenSource.Cancel();
                _ClientWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", new CancellationToken(false));
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

        private void AfterConnect(Task connectTask)
        { 
            if (connectTask.IsCompleted)
            { 
                Task.Run(async () =>
                {
                    _Connected = true;
                    ServerConnected?.Invoke();
                    await DataReceiver(Token);
                }, Token);
            }
            else
            { 
                _Connected = false;
                ServerDisconnected?.Invoke();
            }
        }

        private string BytesToHex(byte[] data)
        {
            if (data == null || data.Length < 1) return "(null)";
            return BitConverter.ToString(data).Replace("-", "");
        }

        private async Task DataReceiver(CancellationToken? cancelToken = null)
        {
            cancelToken = cancelToken ?? CancellationToken.None;
            try
            {
                #region Wait-for-Data
                 
                while (true)
                {
                    cancelToken?.ThrowIfCancellationRequested();

                    byte[] data = await MessageReadAsync(cancelToken.Value); 

                    if (MessageReceived != null)
                    {
                        Task unawaited = Task.Run(() => MessageReceived(data), Token);
                    } 
                }

                #endregion
            }
            catch (OperationCanceledException)
            { 
            }
            catch (WebSocketException)
            { 
            }
            catch (Exception e)
            {
                Log("*** DataReceiver server " + _ServerIp + ":" + _ServerPort + " disconnected");
                LogException("DataReceiver", e);
            }
            finally
            {
                _Connected = false;
                ServerDisconnected?.Invoke();
            }
        }
         
        private async Task<byte[]> MessageReadAsync(CancellationToken token)
        {
            /*
             *
             * Do not catch exceptions, let them get caught by the data reader
             * to destroy the connection
             *
             */

            if (_ClientWs == null) return null;
            byte[] buffer = new byte[65536];
            byte[] data = null;
            WebSocketReceiveResult receiveResult = null;

            using (MemoryStream dataMs = new MemoryStream())
            {
                buffer = new byte[65536];
                ArraySegment<byte> bufferSegment = new ArraySegment<byte>(buffer);

                while (_ClientWs.State == WebSocketState.Open)
                {
                    receiveResult = await _ClientWs.ReceiveAsync(bufferSegment, token);
                    if (receiveResult.Count > 0)
                    {
                        await dataMs.WriteAsync(buffer, 0, receiveResult.Count);
                    }

                    if (receiveResult.EndOfMessage
                        || receiveResult.CloseStatus == WebSocketCloseStatus.Empty
                        || receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        data = dataMs.ToArray();
                        break;
                    }
                }
            }
              
            return data; 
        }
         
        private async Task<bool> MessageWriteAsync(byte[] data)
        {
            bool disconnectDetected = false;

            try
            {
                #region Check-if-Connected

                if (!_Connected
                    || _ClientWs == null)
                {
                    Log("MessageWriteAsync not connected");
                    disconnectDetected = true;
                    return false;
                }

                #endregion

                #region Send-Message

                await SendLock.WaitAsync(Token);
                try
                {
                    await _ClientWs.SendAsync(new ArraySegment<byte>(data, 0, data.Length), WebSocketMessageType.Binary, true, CancellationToken.None);
                }
                catch (Exception eInner)
                {
                    LogException("MessageWriteAsync", eInner);
                }
                finally
                {
                    SendLock.Release();
                }

                return true;

                #endregion
            }
            catch (ObjectDisposedException ObjDispInner)
            {
                Log("*** MessageWriteAsync disconnected (obj disposed exception): " + ObjDispInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (SocketException SockInner)
            {
                Log("*** MessageWriteAsync disconnected (socket exception): " + SockInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (InvalidOperationException InvOpInner)
            {
                Log("*** MessageWriteAsync disconnected (invalid operation exception): " + InvOpInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (IOException IOInner)
            {
                Log("*** MessageWriteAsync disconnected (IO exception): " + IOInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (Exception e)
            {
                LogException("MessageWriteAsync", e);
                disconnectDetected = true;
                return false;
            }
            finally
            {
                if (disconnectDetected)
                {
                    _Connected = false;
                    Dispose();
                    if (ServerDisconnected != null)
                    {
                        Task unawaited = Task.Run(() => ServerDisconnected(), CancellationToken.None);
                    }
                }
            }
        }

        #endregion
    }
}
