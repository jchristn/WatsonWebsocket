namespace WatsonWebsocket
{
    /// <summary>
    /// Util Validator Class.
    /// </summary>
    public static class Validator
    {
        /// <summary>
        /// Validate host name or ip.
        /// </summary>
        /// <param name="host">Host (Url or IP).</param>
        /// <returns>Boolean indicating if the host is valid or not.</returns>
        public static bool IsHost(string host)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(host, @"^(http:\/\/www\.|https:\/\/www\.|http:\/\/|https:\/\/)?[a-z0-9]+([\-\.]{1}[a-z0-9]+)*\.[a-z]{2,5}(:[0-9]{1,5})?(\/.*)?|^((http:\/\/www\.|https:\/\/www\.|http:\/\/|https:\/\/)?([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])\.){3}([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])$"))
                return false;
            return true;
        }

        /// <summary>
        /// Validate port.
        /// </summary>
        /// <param name="port">Port.</param>
        /// <returns>Boolean indicating if the port is valid or not.</returns>
        public static bool IsPort(int port)
        {
            if (port > 65535 || port < 1) return false;
            return true;
        }

    }
}
