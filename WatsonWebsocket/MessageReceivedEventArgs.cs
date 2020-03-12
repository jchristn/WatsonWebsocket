using System;
using System.Collections.Generic;
using System.Text;
using System.Net;

namespace WatsonWebsocket
{
    /// <summary>
    /// Event arguments for when a message is received.
    /// </summary>
    public class MessageReceivedEventArgs : EventArgs
    {
        internal MessageReceivedEventArgs(string ipPort, byte[] data)
        {
            IpPort = ipPort;
            Data = data;
        }

        /// <summary>
        /// The IP:port of the sender.
        /// </summary>
        public string IpPort { get; }

        /// <summary>
        /// The data received.
        /// </summary>
        public byte[] Data { get; }
    }
}