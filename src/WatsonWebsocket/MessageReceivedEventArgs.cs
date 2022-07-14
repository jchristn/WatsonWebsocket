using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.WebSockets;

namespace WatsonWebsocket
{
    /// <summary>
    /// Event arguments for when a message is received.
    /// </summary>
    public class MessageReceivedEventArgs : EventArgs
    {
        #region Public-Members

        /// <summary>
        /// The IP:port of the sender.
        /// </summary>
        public string IpPort { get; } = null;

        /// <summary>
        /// The data received.
        /// </summary>
        public ArraySegment<byte> Data { get; } = default;

        /// <summary>
        /// The type of payload included in the message (Binary or Text).
        /// </summary>
        public WebSocketMessageType MessageType = WebSocketMessageType.Binary;

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        internal MessageReceivedEventArgs(string ipPort, ArraySegment<byte> data, WebSocketMessageType messageType)
        {
            IpPort = ipPort;
            Data = data;
            MessageType = messageType;
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}