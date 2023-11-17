using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SPOLKS_lab
{
    internal class ClientData
    {
        public Socket clientSocket;
        public string filename;
        public string mode;
        public string ipAddres;
        public int port;
        public long index;
        public EndPoint endPoint;

    }
}
