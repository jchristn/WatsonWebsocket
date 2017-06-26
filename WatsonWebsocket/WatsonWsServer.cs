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
using static System.Net.IPAddress;

namespace WatsonWebsocket
{    
    /// <summary>
    /// Watson TCP server.
    /// </summary>
    public class WatsonWsServer : IDisposable
    {
        #region Public-Members

        #endregion

        #region Private-Members

        private readonly bool Debug;
        private readonly string ListenerIp;
        private readonly int ListenerPort;
        private readonly IPAddress ListenerIpAddress;
        private readonly string ListenerPrefix;
        private readonly HttpListener Listener;
        private int _activeClients;
        private readonly ConcurrentDictionary<string, ClientMetadata> Clients;
        private readonly List<string> PermittedIps;
        private readonly CancellationTokenSource TokenSource;
        private CancellationToken _token;
        private readonly Func<string, IDictionary<string, string>, bool> ClientConnected;
        private readonly Func<string, bool> ClientDisconnected;
        private readonly Func<string, byte[], bool> MessageReceived;
        private readonly SemaphoreSlim _sendAsyncLock = new SemaphoreSlim(1);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes the Watson websocket server.
        /// </summary>
        public WatsonWsServer(
            string listenerIp,
            int listenerPort,
            bool ssl,
            bool acceptInvalidCerts,
            IEnumerable<string> permittedIps,
            Func<string, IDictionary<string, string>, bool> clientConnected,
            Func<string, bool> clientDisconnected,
            Func<string, byte[], bool> messageReceived,
            bool debug)
        {
            if (listenerPort < 1) throw new ArgumentOutOfRangeException(nameof(listenerPort));

            ClientConnected = clientConnected ?? null;

            ClientDisconnected = clientDisconnected ?? null;

            PermittedIps = null;

            MessageReceived = messageReceived ?? throw new ArgumentNullException(nameof(MessageReceived));
            Debug = debug;

            if (permittedIps != null && permittedIps.Any()) PermittedIps = new List<string>(permittedIps);

            if (String.IsNullOrEmpty(listenerIp))
            {
                ListenerIpAddress = Loopback;
                ListenerIp = ListenerIpAddress.ToString();
            }
            else
            {
                ListenerIpAddress = Parse(listenerIp);
                ListenerIp = listenerIp;
            }

            ListenerPort = listenerPort;

            if (ssl) ListenerPrefix = "https://" + ListenerIp + ":" + ListenerPort + "/";
            else ListenerPrefix = "http://" + ListenerIp + ":" + ListenerPort + "/";

            if (acceptInvalidCerts) ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;

            Listener = new HttpListener();
            Listener.Prefixes.Add(ListenerPrefix);
            Log("WatsonWsServer starting on " + ListenerPrefix);

            TokenSource = new CancellationTokenSource();
            _token = TokenSource.Token;
            _activeClients = 0;
            Clients = new ConcurrentDictionary<string, ClientMetadata>();
            Task.Run(AcceptConnections, _token);
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
        /// Send data to the specified client, asynchronously.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string ipPort, byte[] data)
        {
            ClientMetadata client;
            if (!Clients.TryGetValue(ipPort, out client))
            {
                Log("Send unable to find client " + ipPort);
                return false;
            }

            return await MessageWriteAsync(client, data);
        }

        /// <summary>
        /// Determine whether or not the specified client is connected to the server.
        /// </summary>
        /// <returns>Boolean indicating if the client is connected to the server.</returns>
        public bool IsClientConnected(string ipPort)
        {
            ClientMetadata client;
            return (Clients.TryGetValue(ipPort, out client));
        }

        /// <summary>
        /// List the IP:port of each connected client.
        /// </summary>
        /// <returns>A string list containing each client IP:port.</returns>
        public List<string> ListClients()
        {
            Dictionary<string, ClientMetadata> clients = Clients.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            List<string> ret = new List<string>();
            foreach (KeyValuePair<string, ClientMetadata> curr in clients)
            {
                ret.Add(curr.Key);
            }
            return ret;
        }

        #endregion

        #region Private-Methods

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Listener?.Stop();
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

        private async Task AcceptConnections()
        {
            try
            {
                #region Accept-WS-Connections

                Listener.Start();
                
                while (!_token.IsCancellationRequested)
                { 
                    #region Accept-Connection

                    HttpListenerContext httpContext = await Listener.GetContextAsync();

                    #endregion

                    #region Get-Tuple-and-Check-IP

                    if (httpContext.Request.RemoteEndPoint != null)
                    {
                        string clientIp = httpContext.Request.RemoteEndPoint.Address.ToString();
                        int clientPort = httpContext.Request.RemoteEndPoint.Port;

                        var query = new Dictionary<string, string>();
                        var requestQuery = httpContext.Request.QueryString;
                        foreach (var key in requestQuery.AllKeys)
                        {
                            query[key] = requestQuery[key];
                        }

                        if (PermittedIps != null && PermittedIps.Count > 0)
                        {
                            if (!PermittedIps.Contains(clientIp))
                            {
                                Log("*** AcceptConnections rejecting connection from " + clientIp + " (not permitted)");
                                httpContext.Response.StatusCode = 401;
                                httpContext.Response.Close();
                                return;
                            }
                        }

                        Log("AcceptConnections accepted connection from " + clientIp + ":" + clientPort);

                        #endregion

                        #region Get-Websocket-Context

                        WebSocketContext wsContext = null;
                        try
                        {
                            wsContext = httpContext.AcceptWebSocketAsync(subProtocol: null).Result;
                        }
                        catch (Exception)
                        {
                            Log("*** AcceptConnections unable to retrieve websocket content for client " + clientIp + ":" + clientPort);
                            httpContext.Response.StatusCode = 500;
                            httpContext.Response.Close();
                            return;
                        }

                        WebSocket ws = wsContext.WebSocket;

                        #endregion

                        var unawaited = Task.Run(() =>
                        { 
                            #region Add-to-Client-List

                            _activeClients++;
                            // Do not decrement in this block, decrement is done by the connection reader

                            ClientMetadata currClient = new ClientMetadata(httpContext, ws, wsContext);
                            if (!AddClient(currClient))
                            {
                                Log("*** AcceptConnections unable to add client " + clientIp + ":" + clientPort);
                                httpContext.Response.StatusCode = 500;
                                httpContext.Response.Close();
                                return;
                            }

                            #endregion

                            #region Start-Data-Receiver

                            CancellationToken dataReceiverToken = default(CancellationToken);

                            Log("AcceptConnections starting data receiver for " + clientIp + ":" + clientPort + " (now " + _activeClients + " clients)");
                            if (ClientConnected != null)
                            {
                                Task.Run(() => ClientConnected(clientIp + ":" + clientPort, query), dataReceiverToken);
                            }

                            Task.Run(async () => await DataReceiver(currClient, dataReceiverToken), dataReceiverToken);

                            #endregion 

                        }, _token);
                    }
                }

                #endregion
            }
            catch (Exception e)
            {
                LogException("AcceptConnections", e);
            }
        }
        
        private async Task DataReceiver(ClientMetadata client, CancellationToken? cancelToken = null)
        { 
            try
            {
                #region Wait-for-Data

                while (true)
                {
                    cancelToken?.ThrowIfCancellationRequested();
                    
                    byte[] data = await MessageReadAsync(client);
                    if (data == null)
                    {
                        // no message available
                        await Task.Delay(30, _token);
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
                        var unawaited = Task.Run(() => MessageReceived(client.IpPort(), data));
                    }
                }

                #endregion
            }
            catch (OperationCanceledException oce)
            {
                Log("DataReceiver client " + client.IpPort() + " disconnected (canceled): " + oce.Message);
                throw; //normal cancellation
            }
            catch (WebSocketException wse)
            {
                Log("DataReceiver client " + client.IpPort() + " disconnected (websocket exception): " + wse.Message);
            }
            finally
            {
                _activeClients--;
                RemoveClient(client);
                if (ClientDisconnected != null)
                {
                    var unawaited = Task.Run(() => ClientDisconnected(client.IpPort()), _token);
                }
                Log("DataReceiver client " + client.IpPort() + " disconnected (now " + _activeClients + " clients active)");
            }
        }

        private bool AddClient(ClientMetadata client)
        { 
            ClientMetadata removedClient;
            if (!Clients.TryRemove(client.IpPort(), out removedClient))
            {
                // do nothing, it probably did not exist anyway
            }

            Clients.TryAdd(client.IpPort(), client);
            Log("AddClient added client " + client.IpPort());
            return true;
        }

        private bool RemoveClient(ClientMetadata client)
        { 
            ClientMetadata removedClient;
            if (!Clients.TryRemove(client.IpPort(), out removedClient))
            {
                Log("RemoveClient unable to remove client " + client.IpPort());
                return false;
            }
            else
            {
                Log("RemoveClient removed client " + client.IpPort());
                return true;
            }
        }

        private async Task<byte[]> MessageReadAsync(ClientMetadata client)
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

            if (client.HttpContext == null) return null;
            if (client.WsContext == null) return null;

            #endregion

            #region Read-Header

            using (MemoryStream headerMs = new MemoryStream())
            {
                #region Read-Header-Bytes

                byte[] headerBuffer = new byte[1];
                timeout = false;
                currentTimeout = 0;
                int read = 0;

                while (client.Ws.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult receiveResult = await client.Ws.ReceiveAsync(new ArraySegment<byte>(headerBuffer), CancellationToken.None);
                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        await client.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
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
                    Log("*** MessageReadAsync from " + client.IpPort() + " timeout " + currentTimeout + "ms/" + maxTimeout + "ms exceeded while reading header after reading " + bytesRead + " bytes");
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
                        Log("MessageReadAsync received termination notice from client " + client.IpPort());
                        return headerBytes;
                    }
                }

                if (!Int64.TryParse(header, out contentLength))
                {
                    Log("*** MessageReadAsync malformed message from " + client.IpPort() + " (message header not an integer): " + BytesToHex(headerBytes));
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

                while (client.Ws.State == WebSocketState.Open)
                {
                    if (bufferSize > bytesRemaining) bufferSize = bytesRemaining;
                    buffer = new byte[bufferSize];

                    WebSocketReceiveResult receiveResult = await client.Ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
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
                Log("*** MessageReadAsync " + client.IpPort() + " no content read");
                return null;
            }

            if (contentBytes.Length != contentLength)
            {
                Log("*** MessageReadAsync " + client.IpPort() + " content length " + contentBytes.Length + " bytes does not match header value " + contentLength + ", discarding");
                return null;
            }

            #endregion

            return contentBytes;
        }

        private async Task<bool> MessageWriteAsync(ClientMetadata client, byte[] data)
        { 
            try
            {
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

                // can't have two simultaneous SendAsync calls so use a semaphore to block the second until the first has completed
                await _sendAsyncLock.WaitAsync(_token);
                if (_token.IsCancellationRequested)
                {
                    return false;
                }
                try
                {
                    await client.Ws.SendAsync(new ArraySegment<byte>(message, 0, message.Length),
                        WebSocketMessageType.Binary, true, CancellationToken.None);
                }
                finally
                {
                    _sendAsyncLock.Release();
                }
                return true;

                #endregion
            }
            catch (Exception)
            {
                Log("*** MessageWriteAsync " + client.IpPort() + " disconnected due to exception");
                return false;
            }
        }
         
        #endregion
    }
}
