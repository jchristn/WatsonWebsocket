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
                return _Connected;
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

        #endregion

        #region Private-Members

        private bool _AcceptInvalidCertificates = true;
        private Uri _ServerUri;
        private string _ServerIp;
        private int _ServerPort;
        private string _ServerIpPort;
        private string _Url;
        private ClientWebSocket _ClientWs;
        private bool _Connected = false; 

        private readonly SemaphoreSlim _SendLock = new SemaphoreSlim(1);
        private CancellationTokenSource _TokenSource = new CancellationTokenSource();
        private CancellationToken _Token;

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
                _TokenSource.Cancel();
                _ClientWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", new CancellationToken(false));
            }
        }
         
        private void AfterConnect(Task connectTask)
        { 
            if (connectTask.IsCompleted)
            {
                if (_ClientWs.State == WebSocketState.Open)
                {
                    Task.Run(async () =>
                    {
                        _Connected = true;
                        ServerConnected?.Invoke(this, EventArgs.Empty);
                        await DataReceiver();
                    }, _Token);
                }
                else
                {
                    _Connected = false;
                    ServerDisconnected?.Invoke(this, EventArgs.Empty);
                }
            }
            else
            { 
                _Connected = false;
                ServerDisconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        private string BytesToHex(byte[] data)
        {
            if (data == null || data.Length < 1) return "(null)";
            return BitConverter.ToString(data).Replace("-", "");
        }

        private async Task DataReceiver()
        {
            string header = "[WatsonWsClient.DataReceiver " + _ServerIpPort + "] "; 

            try
            {
                #region Wait-for-Data
                 
                while (true)
                {
                    if (_Token.IsCancellationRequested) break;
                    byte[] data = await MessageReadAsync();
                    MessageReceived?.Invoke(this, new MessageReceivedEventArgs(_ServerIpPort, data));
                }

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

            _Connected = false;
            ServerDisconnected?.Invoke(this, EventArgs.Empty);
        }
         
        private async Task<byte[]> MessageReadAsync()
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
                    receiveResult = await _ClientWs.ReceiveAsync(bufferSegment, _Token);
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
            string header = "[WatsonWsClient.DataReceiver " + _ServerIpPort + "] ";
            bool disconnectDetected = false;

            try
            {
                #region Check-if-Connected

                if (!_Connected
                    || _ClientWs == null)
                {
                    Logger?.Invoke(header + "not connected");
                    disconnectDetected = true;
                    return false;
                }

                #endregion

                #region Send-Message

                await _SendLock.WaitAsync(_Token);

                try
                {
                    await _ClientWs.SendAsync(new ArraySegment<byte>(data, 0, data.Length), WebSocketMessageType.Binary, true, CancellationToken.None);
                }
                catch (OperationCanceledException oce)
                {
                    Logger?.Invoke(header + "disconnected (canceled): " + oce.Message);
                    return false;
                }
                catch (WebSocketException wse)
                {
                    Logger?.Invoke(header + "disconnected (websocket exception): " + wse.Message);
                    return false;
                }
                catch (Exception e)
                {
                    Logger?.Invoke(header + "disconnected due to exception: " + Environment.NewLine + e.ToString());
                    return false;
                }
                finally
                {
                    _SendLock.Release();
                }

                return true;

                #endregion
            }
            catch (OperationCanceledException oce)
            {
                Logger?.Invoke(header + "disconnected (canceled): " + oce.Message);
                disconnectDetected = true;
                return false;
            }
            catch (WebSocketException wse)
            {
                Logger?.Invoke(header + "disconnected (websocket exception): " + wse.Message);
                disconnectDetected = true;
                return false;
            }
            catch (ObjectDisposedException ObjDispInner)
            {
                Logger?.Invoke(header + "disconnected (obj disposed exception): " + ObjDispInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (SocketException sockInner)
            {
                Logger?.Invoke(header + "disconnected (socket exception): " + sockInner.Message); 
                disconnectDetected = true;
                return false;
            }
            catch (InvalidOperationException invOpInner)
            {
                Logger?.Invoke(header + "disconnected (invalid operation exception): " + invOpInner.Message); 
                disconnectDetected = true;
                return false;
            }
            catch (IOException ioInner)
            {
                Logger?.Invoke(header + "disconnected (IO exception): " + ioInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (Exception e)
            {
                Logger?.Invoke(header + "disconnected due to exception: " + Environment.NewLine + e.ToString());
                disconnectDetected = true;
                return false;
            }
            finally
            {
                if (disconnectDetected)
                {
                    _Connected = false;
                    Dispose();

                    ServerDisconnected?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        #endregion
    }
}
