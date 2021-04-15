using System;
using System.Collections.Generic;
using System.Text;
using System.Net;

namespace WatsonWebsocket
{
    /// <summary>
    /// Event arguments for when a client connects to the server.
    /// </summary>
    public class ClientConnectedEventArgs : EventArgs
    {
        #region Public-Members

        /// <summary>
        /// The IP:port of the client.
        /// </summary>
        public string IpPort { get; } = null;

        /// <summary>
        /// The HttpListenerRequest from the client.  Helpful for accessing HTTP request related metadata such as the querystring.
        /// </summary>
        public HttpListenerRequest HttpRequest { get; } = null;

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        internal ClientConnectedEventArgs(string ipPort, HttpListenerRequest http)
        {
            IpPort = ipPort;
            HttpRequest = http;
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}