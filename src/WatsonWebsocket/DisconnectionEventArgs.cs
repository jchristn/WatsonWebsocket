using System;
using System.Collections.Generic;
using System.Text;
using System.Net;

namespace WatsonWebsocket
{
    /// <summary>
    /// Event arguments for when a client disconnects from the server.
    /// </summary>
    public class DisconnectionEventArgs : EventArgs
    {
        #region Public-Members

        /// <summary>
        /// Client metadata.
        /// </summary>
        public ClientMetadata Client { get; } = null;

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        internal DisconnectionEventArgs(ClientMetadata client)
        {
            Client = client;
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}