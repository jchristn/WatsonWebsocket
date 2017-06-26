using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
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

        #endregion

        #region Private-Members

        private string ServerIp;
        private int ServerPort;
        private bool Debug;
        private string Url;
        private ClientWebSocket ClientWs;
        private bool Connected;
        private Func<byte[], bool> MessageReceived;
        private Func<bool> ServerConnected;
        private Func<bool> ServerDisconnected;

        private readonly SemaphoreSlim SendLock;
        private CancellationTokenSource TokenSource;
        private CancellationToken Token;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes the Watson websocket client.
        /// </summary>
        public WatsonWsClient(
            string serverIp,
            int serverPort,
            bool ssl,
            bool acceptInvalidCerts,
            Func<bool> serverConnected,
            Func<bool> serverDisconnected,
            Func<byte[], bool> messageReceived,
            bool debug)
        {
            if (String.IsNullOrEmpty(serverIp)) throw new ArgumentNullException(nameof(serverIp));
            if (serverPort < 1) throw new ArgumentOutOfRangeException(nameof(serverPort));
            if (messageReceived == null) throw new ArgumentNullException(nameof(messageReceived));

            if (serverConnected != null) ServerConnected = serverConnected;
            else ServerConnected = null;

            if (serverDisconnected != null) ServerDisconnected = serverDisconnected;
            else ServerDisconnected = null;

            ServerIp = serverIp;
            ServerPort = serverPort;
            Debug = debug;
            MessageReceived = messageReceived;
            SendLock = new SemaphoreSlim(1);

            if (ssl) Url = "wss://" + ServerIp + ":" + ServerPort;
            else Url = "ws://" + ServerIp + ":" + ServerPort;

            if (acceptInvalidCerts) ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;
            ClientWs = new ClientWebSocket();
            ClientWs.ConnectAsync(new Uri(Url), CancellationToken.None).Wait();

            Connected = true;
            Task.Run(() => ServerConnected?.Invoke());
            
            TokenSource = new CancellationTokenSource();
            Token = TokenSource.Token;
            Task.Run(async () => await DataReceiver(Token), Token);
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
        /// Send data to the server asynchronously
        /// </summary>
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(byte[] data)
        {
            return await MessageWriteAsync(data);
        }

        /// <summary>
        /// Determine whether or not the client is connected to the server.
        /// </summary>
        /// <returns>Boolean indicating if the client is connected to the server.</returns>
        public bool IsConnected()
        {
            return Connected;
        }

        #endregion

        #region Private-Methods

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                ClientWs?.Abort();
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

        private async Task DataReceiver(CancellationToken? cancelToken = null)
        {
            try
            {
                #region Wait-for-Data

                while (true)
                {
                    cancelToken?.ThrowIfCancellationRequested();
                     
                    byte[] data = await MessageReadAsync();
                    if (data == null)
                    {
                        // no message available
                        await Task.Delay(30, Token);
                        continue;
                    }
                    else
                    {
                        if (data.Length == 1 && data[0] == 0x00)
                        {
                            break;
                        }
                    }

                    if (MessageReceived != null)
                    {
                        var unawaited = Task.Run(() => MessageReceived(data), Token);
                    } 
                }

                #endregion
            }
            catch (OperationCanceledException oce)
            {
                Log("*** DataReceiver server disconnected (canceled): " + oce.Message);
            }
            catch (WebSocketException wse)
            {
                Log("*** DataReceiver server disconnected (websocket exception): " + wse.Message);
            }
            catch (Exception)
            {
                Log("*** DataReceiver server " + ServerIp + ":" + ServerPort + " disconnected");
            }
            finally
            {
                Connected = false;
                ServerDisconnected?.Invoke();
            }
        }
         
        private async Task<byte[]> MessageReadAsync()
        { 
            /*
             *
             * Do not catch exceptions, let them get caught by the data reader
             * to destroy the connection
             *
             */

            #region Variables

            int bytesRead = 0;
            int sleepInterval = 25;
            int maxTimeout = 500;
            int currentTimeout = 0;
            bool timeout = false;

            byte[] headerBytes;
            string header = "";
            long contentLength;
            byte[] contentBytes;

            if (ClientWs == null) return null;

            #endregion

            #region Read-Header

            using (MemoryStream headerMs = new MemoryStream())
            {
                #region Read-Header-Bytes

                byte[] headerBuffer = new byte[1];
                timeout = false;
                currentTimeout = 0;
                int read = 0;

                while (ClientWs.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult receiveResult = await ClientWs.ReceiveAsync(new ArraySegment<byte>(headerBuffer), CancellationToken.None);
                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        await ClientWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    }

                    headerMs.Write(headerBuffer, 0, 1);
                    read++;

                    if (read > 0)
                    {
                        bytesRead += read;

                        if (bytesRead > 1)
                        {
                            // check if end of headers reached
                            if ((int)headerBuffer[0] == 58) break;
                        }
                    }
                    else
                    {
                        if (currentTimeout >= maxTimeout)
                        {
                            timeout = true;
                            break;
                        }
                        else
                        {
                            currentTimeout += sleepInterval;
                            Task.Delay(sleepInterval).Wait();
                        }
                    }
                }

                if (timeout)
                {
                    Log("*** MessageReadAsync timeout " + currentTimeout + "ms/" + maxTimeout + "ms exceeded while reading header after reading " + bytesRead + " bytes");
                    return null;
                }

                headerBytes = headerMs.ToArray();
                if (headerBytes == null || headerBytes.Length < 1)
                {
                    return null;
                }

                #endregion

                #region Process-Header

                header = Encoding.UTF8.GetString(headerBytes);
                header = header.Replace(":", "");

                if (header.Length == 1)
                {
                    if (header[0] == 0x00)
                    {
                        Log("MessageReadAsync received termination notice from server");
                        return headerBytes;
                    }
                }

                if (!Int64.TryParse(header, out contentLength))
                {
                    Log("*** MessageReadAsync malformed message from server (message header not an integer): " + BytesToHex(headerBytes));
                    return null;
                }

                #endregion
            }

            #endregion

            #region Read-Data

            using (MemoryStream dataMs = new MemoryStream())
            {
                long bytesRemaining = contentLength;
                timeout = false;
                currentTimeout = 0;

                int read = 0;
                byte[] buffer;
                long bufferSize = 2048;

                while (ClientWs.State == WebSocketState.Open)
                {
                    if (bufferSize > bytesRemaining) bufferSize = bytesRemaining;
                    buffer = new byte[bufferSize];

                    WebSocketReceiveResult receiveResult = await ClientWs.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        //
                        // end of message
                        //
                    }
                    else
                    {
                        dataMs.Write(buffer, 0, buffer.Length);
                        read += buffer.Length;
                        bytesRemaining -= buffer.Length;

                        // check if read fully
                        if (bytesRemaining == 0) break;
                        if (read == contentLength) break;
                    }
                }

                contentBytes = dataMs.ToArray();
            }

            #endregion

            #region Check-Content-Bytes

            if (contentBytes == null || contentBytes.Length < 1)
            {
                Log("*** MessageReadAsync no content read");
                return null;
            }

            if (contentBytes.Length != contentLength)
            {
                Log("*** MessageReadAsync content length " + contentBytes.Length + " bytes does not match header value " + contentLength + ", discarding");
                return null;
            }

            #endregion

            return contentBytes; 
        }
         
        private async Task<bool> MessageWriteAsync(byte[] data)
        {
            bool disconnectDetected = false;

            try
            {
                #region Check-if-Connected

                if (ClientWs == null)
                {
                    Log("MessageWriteAsync client is null");
                    disconnectDetected = true;
                    return false;
                }

                #endregion

                #region Format-Message

                string header = "";
                byte[] headerBytes;
                byte[] message;

                if (data == null || data.Length < 1) header += "0:";
                else header += data.Length + ":";

                headerBytes = Encoding.UTF8.GetBytes(header);
                int messageLen = headerBytes.Length;
                if (data != null && data.Length > 0) messageLen += data.Length;

                message = new byte[messageLen];
                Buffer.BlockCopy(headerBytes, 0, message, 0, headerBytes.Length);

                if (data != null && data.Length > 0) Buffer.BlockCopy(data, 0, message, headerBytes.Length, data.Length);

                #endregion

                #region Send-Message

                await SendLock.WaitAsync();
                try
                {
                    await ClientWs.SendAsync(new ArraySegment<byte>(message, 0, message.Length), WebSocketMessageType.Binary, true, CancellationToken.None);
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
                    Connected = false;
                    Dispose();
                    if (ServerDisconnected != null)
                    {
                        var unawaited = Task.Run(() => ServerDisconnected());
                    }
                }
            }
        }

        #endregion
    }
}
