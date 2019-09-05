using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;

namespace WatsonWebsocket
{
    internal class ClientMetadata
    {
        #region Internal-Members

        internal string Ip;
        internal int Port;
        internal HttpListenerContext HttpContext;
        internal WebSocket Ws;
        internal WebSocketContext WsContext;
        internal readonly CancellationTokenSource KillToken;
        internal readonly SemaphoreSlim SendAsyncLock = new SemaphoreSlim(1);

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories
         
        internal ClientMetadata(HttpListenerContext httpContext, WebSocket ws, WebSocketContext wsContext, CancellationTokenSource killTokenSource)
        {
            HttpContext = httpContext ?? throw new ArgumentNullException(nameof(httpContext));
            Ws = ws ?? throw new ArgumentNullException(nameof(ws));
            WsContext = wsContext ?? throw new ArgumentNullException(nameof(wsContext));
            KillToken = killTokenSource ?? throw new ArgumentNullException(nameof(killTokenSource));
             
            Ip = HttpContext.Request.RemoteEndPoint.Address.ToString();
            Port = HttpContext.Request.RemoteEndPoint.Port;
        }

        #endregion

        #region Internal-Methods

        internal string IpPort()
        {
            return Ip + ":" + Port;
        }

        #endregion

        #region Private-Methods

        #endregion
    }
}
