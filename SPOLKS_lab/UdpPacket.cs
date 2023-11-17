using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SPOLKS_lab
{
    internal class UdpPacket
    {
        public int byteHashCode;

        public int stringHashCode;

        public string message;

        public byte[] data;

        public DateTime sentTime;



        public UdpPacket(string msg)
        {
            this.stringHashCode = Program.CustomStringHashCode(msg);
            this.message = msg;
            this.data = Encoding.ASCII.GetBytes(message);
            this.byteHashCode = data.GetHashCode();
            this.sentTime = DateTime.Now;
        }

        public UdpPacket(byte[] bytes)
        {
            this.data = bytes;
            this.byteHashCode = data.GetHashCode();
            this.sentTime = DateTime.Now;
        }
    }
}
