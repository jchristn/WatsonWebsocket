using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;

namespace WatsonWebsocket
{
    public class ClientMetadata
    {
        #region Public-Members

        public string Ip;
        public int Port;
        public HttpListenerContext HttpContext;
        public WebSocket Ws;
        public WebSocketContext WsContext;
        public readonly CancellationTokenSource KillToken;
        public readonly SemaphoreSlim SendAsyncLock = new SemaphoreSlim(1);

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        public ClientMetadata()
        {

        }
         
        public ClientMetadata(HttpListenerContext httpContext, WebSocket ws, WebSocketContext wsContext, CancellationTokenSource killTokenSource)
        {
            HttpContext = httpContext ?? throw new ArgumentNullException(nameof(httpContext));
            Ws = ws ?? throw new ArgumentNullException(nameof(ws));
            WsContext = wsContext ?? throw new ArgumentNullException(nameof(wsContext));
            KillToken = killTokenSource ?? throw new ArgumentNullException(nameof(killTokenSource));
             
            Ip = HttpContext.Request.RemoteEndPoint.Address.ToString();
            Port = HttpContext.Request.RemoteEndPoint.Port;
        }

        #endregion

        #region Public-Methods

        public string IpPort()
        {
            return Ip + ":" + Port;
        }

        #endregion

        #region Private-Methods

        #endregion
    }
}
