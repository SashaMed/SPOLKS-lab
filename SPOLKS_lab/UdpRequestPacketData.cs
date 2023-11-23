using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace SPOLKS_lab
{
    internal class UdpRequestPacketData
    {
        public byte[] Buffer { get; set; }
        public EndPoint ClientEndPoint { get; set; }
        public int ReceivedBytes { get; set; }
    }
}
