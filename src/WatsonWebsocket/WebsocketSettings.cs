namespace WatsonWebsocket
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Text;

    /// <summary>
    /// Websocket settings.
    /// </summary>
    public class WebsocketSettings
    {
        #region Public-Members

        /// <summary>
        /// Hostnames on which to listen.
        /// </summary>
        public List<string> Hostnames
        {
            get => _Hostnames;
            set => _Hostnames = value ?? new List<string>();
        }

        /// <summary>
        /// Port on which to listen.
        /// </summary>
        public int Port
        {
            get => _Port;
            set => _Port = (value >= 0 && value < 65536) ? value : throw new ArgumentOutOfRangeException(nameof(Port));
        }

        /// <summary>
        /// Boolean indicating whether or not SSL should be used.
        /// </summary>
        public bool Ssl { get; set; } = false;

        #endregion

        #region Private-Members

        private List<string> _Hostnames = new List<string>();
        private int _Port = 8000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Websocket settings.
        /// </summary>
        public WebsocketSettings()
        {

        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
