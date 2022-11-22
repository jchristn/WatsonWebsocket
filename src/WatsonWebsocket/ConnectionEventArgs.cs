using System;
using System.Collections.Generic;
using System.Text;
using System.Net;

namespace WatsonWebsocket
{
    /// <summary>
    /// Event arguments for when a client connects to the server.
    /// </summary>
    public class ConnectionEventArgs : EventArgs
    {
        #region Public-Members

        /// <summary>
        /// Client metadata.
        /// </summary>
        public ClientMetadata Client { get; } = null;

        /// <summary>
        /// The HttpListenerRequest from the client.  Helpful for accessing HTTP request related metadata such as the querystring.
        /// </summary>
        public HttpListenerRequest HttpRequest { get; } = null;

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        internal ConnectionEventArgs(ClientMetadata client, HttpListenerRequest http)
        {
            Client = client;
            HttpRequest = http;
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}