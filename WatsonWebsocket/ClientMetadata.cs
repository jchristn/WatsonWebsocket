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
        public readonly SemaphoreSlim SendAsyncLock = new SemaphoreSlim(1);

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        public ClientMetadata()
        {

        }
         
        public ClientMetadata(HttpListenerContext httpContext, WebSocket ws, WebSocketContext wsContext)
        {
            if (httpContext == null) throw new ArgumentNullException(nameof(httpContext));
            if (ws == null) throw new ArgumentNullException(nameof(ws));
            if (wsContext == null) throw new ArgumentNullException(nameof(wsContext));

            HttpContext = httpContext;
            Ws = ws;
            WsContext = wsContext;
             
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
