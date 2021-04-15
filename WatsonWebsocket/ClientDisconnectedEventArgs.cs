using System;
using System.Collections.Generic;
using System.Text;
using System.Net;

namespace WatsonWebsocket
{
    /// <summary>
    /// Event arguments for when a client disconnects from the server.
    /// </summary>
    public class ClientDisconnectedEventArgs : EventArgs
    {
        #region Public-Members

        /// <summary>
        /// The IP:port of the client.
        /// </summary>
        public string IpPort { get; } = null;

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        internal ClientDisconnectedEventArgs(string ipPort)
        {
            IpPort = ipPort;
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}