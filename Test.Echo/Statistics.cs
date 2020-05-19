namespace Test.Echo
{
    internal class Statistics
    {
        public long MsgSent
        {
            get
            {
                lock (_StatsLock)
                {
                    return _MsgSent;
                }
            }
        }

        public long MsgRecv
        {
            get
            {
                lock (_StatsLock)
                {
                    return _MsgRecv;
                }
            }
        }

        public long BytesSent
        {
            get
            {
                lock (_StatsLock)
                {
                    return _BytesSent;
                }
            }
        }

        public long BytesRecv
        {
            get
            {
                lock (_StatsLock)
                {
                    return _BytesRecv;
                }
            }
        }

        private readonly object _StatsLock = new object();

        private long _MsgSent = 0;
        private long _MsgRecv = 0;
        private long _BytesSent = 0;
        private long _BytesRecv = 0;

        public Statistics()
        {
            _MsgSent = 0;
            _MsgRecv = 0;
            _BytesSent = 0;
            _BytesRecv = 0;
        }

        public override string ToString()
        {
            return "Sent [" + _MsgSent + " msgs, " + _BytesSent + " bytes] Received [" + _MsgRecv + " msgs, " + _BytesRecv + " bytes]";
        }

        public void AddSent(long len)
        {
            lock (_StatsLock)
            {
                _MsgSent++;
                _BytesSent += len;
            }
        }

        public void AddRecv(long len)
        {
            lock (_StatsLock)
            {
                _MsgRecv++;
                _BytesRecv += len;
            }
        }
    }
}