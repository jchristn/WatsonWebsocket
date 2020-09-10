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
        internal MessageReceivedEventArgs(string ipPort, byte[] data, WebSocketMessageType messageType)
        {
            IpPort = ipPort;
            Data = data;
            MessageType = messageType;
        }

        /// <summary>
        /// The IP:port of the sender.
        /// </summary>
        public string IpPort { get; }

        /// <summary>
        /// The data received.
        /// </summary>
        public byte[] Data { get; }

        /// <summary>
        /// The type of payload included in the message (Binary or Text).
        /// </summary>
        public WebSocketMessageType MessageType = WebSocketMessageType.Binary;
    }
}