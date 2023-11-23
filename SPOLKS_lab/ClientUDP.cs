using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

namespace SPOLKS_lab
{
    internal class ClientUDP
    {

        private Socket clientSocket;
        private string serverIp;
        private string clientIp;
        private int serverPort;
        private int clientPort;
        private bool connectionClose;
        private bool canSendToServer;
        private IPEndPoint serverEndPoint;
        private List<UdpPacket> packets;

        public ClientUDP(string serverIp, int serverPort, string clientIp, int clientPort)
        {
            canSendToServer = true;
            this.serverIp = serverIp;
            this.serverPort = serverPort;
            this.clientIp = clientIp;
            this.clientPort = clientPort;
            IPAddress ipAddress = IPAddress.Parse(clientIp);
            IPEndPoint localEndPoint = new IPEndPoint(ipAddress, clientPort);
            packets = new List<UdpPacket>();

            serverEndPoint = new IPEndPoint(IPAddress.Parse(serverIp), serverPort);
            clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            try
            {

                clientSocket.Bind(localEndPoint);
                //Console.WriteLine($"Socket connected to {clientSocket.RemoteEndPoint.ToString()}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Unable to bind to remote endpoint: {e.ToString()}");
            }
        }


        public ClientUDP(string serverIp, int serverPort)
        {
            canSendToServer = true;
            this.serverIp = serverIp;
            this.serverPort = serverPort;
            packets = new List<UdpPacket>();
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 12367);
            clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            serverEndPoint = new IPEndPoint(IPAddress.Parse(serverIp), serverPort);

            try
            {
                clientSocket.Bind(localEndPoint);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Unable to bind to remote endpoint: {e.ToString()}");
            }
        }



        public void Start()
        {
            try
            {
                while (true)
                {
                    //if (connectionClose)
                    //{
                    //    break;
                    //}
                    InputUpdate();
                }

                clientSocket.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine("\n--------------------------------------------");
                Console.WriteLine("Error: " + e);
                Console.WriteLine("--------------------------------------------");
                clientSocket.Close();
            }
        }

        private void InputUpdate()
        {
            if (connectionClose)
            {
                return;
            }
            Console.Write(">>");
            string message = Console.ReadLine();
            if (string.IsNullOrEmpty(message))
            {
                return;
            }
            var splitedMessage = message.Split(' ');
            if (splitedMessage.Length > 1)
            {
                if (splitedMessage[0] == "upload")
                {
                    UploadToServer(splitedMessage[1]);
                    return;
                }
                else if (splitedMessage[0] == "download")
                {
                    DownloadFromServer(message);
                    return;
                }

            }
            //byte[] data = Encoding.ASCII.GetBytes(message);
            if (connectionClose)
            {
                return;
            }
            if (!canSendToServer)
            {
                Console.WriteLine("cant send to server now");
                return;
            }
            Send(message);
            Read();
        }


        public void Send(string message)
        {
            try
            {
                byte[] data = Encoding.ASCII.GetBytes(message);
                clientSocket.SendTo(data, serverEndPoint);

                EndPoint clientEndPoint = new IPEndPoint(IPAddress.Any, 0);
                var buffer = new byte[Program.bufferSize];
                var receivedBytes = clientSocket.ReceiveFrom(buffer, ref clientEndPoint);
                var response = Encoding.ASCII.GetString(buffer, 0, receivedBytes);
                Console.WriteLine($"Received ack: {response}");

            }
            catch (SocketException e)
            {
                Console.WriteLine("--------------------------------------------");
                Console.WriteLine("SocketException while sending udp packet: " + e);
                Console.WriteLine("--------------------------------------------");
            }
        }


        private string Read()
        {
            EndPoint clientEndPoint = new IPEndPoint(IPAddress.Any, 0);
            var buffer = new byte[Program.bufferSize];
            var receivedBytes = clientSocket.ReceiveFrom(buffer, ref clientEndPoint);
            var response = Encoding.ASCII.GetString(buffer, 0, receivedBytes);
            Console.WriteLine($"Received: {response} from {clientEndPoint}");

            var packet = new UdpPacket(response);
            var stringACK = $"ACK {packet.stringHashCode}";
            Console.WriteLine("Sent " + stringACK);
            byte[] data = Encoding.ASCII.GetBytes(stringACK);
            clientSocket.SendTo(data, serverEndPoint);
            return response;
        }


        private bool WaitForAck(List<UdpPacket> windowPackets, int waitTime)
        {
            int ackCount = 0;
            int expectedAcks = windowPackets.Count; 
            const int timeout = 5000; 

            clientSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, timeout);

            try
            {
                for (int i = 0; i < expectedAcks; i++)
                {
                    EndPoint clientEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    byte[] buffer = new byte[Program.bufferSize];
                    clientSocket.ReceiveFrom(buffer, ref clientEndPoint);

                    string response = Encoding.ASCII.GetString(buffer);
                    if (response.StartsWith("ACK"))
                    {
                        //Console.WriteLine($"Received ack: " + response);
                        ackCount++;
                    }
                }
            }
            catch (SocketException e)
            {
                if (e.SocketErrorCode == SocketError.TimedOut)
                {
                    Console.WriteLine("Timeout while waiting for ACK");
                    return false; 
                }
                throw; 
            }

            return ackCount == expectedAcks; 
        }

        private void UploadToServer(string fileName, ulong fileOffset = 0)
        {
            if (!File.Exists(fileName))
            {
                string message = "File does not exist";
                Console.WriteLine(message);
                return;
            }
            try
            {
                var uploadRequest = "upload " + fileName;
                Send(uploadRequest);
                var uploadResponse = Read();
                var splitedResponse = uploadResponse.Split(' ');
                Console.WriteLine("response length: " + splitedResponse.Length);
                foreach(var item in splitedResponse)
                {
                    Console.WriteLine(item);
                }
                fileOffset = ulong.Parse(splitedResponse[2]);
                const int windowSize = 5; 
                int packetSize = Program.bufferSize;
                int waitTime = 5;
                List<UdpPacket> windowPackets = new List<UdpPacket>(); 
                int currentPacketIndex = 0; 

                using (FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read))
                {
                    if (fileOffset != 0)
                    {
                        fs.Seek((long)fileOffset, SeekOrigin.Begin);
                    }
                    currentPacketIndex = (int)(fileOffset / (ulong)packetSize); 

                    int totalPackets = (int)Math.Ceiling((double)fs.Length / packetSize);
                    byte[] buffer = new byte[packetSize];
                    int bytesRead;

                    while (currentPacketIndex < totalPackets)
                    {
                        while (windowPackets.Count < windowSize && (bytesRead = fs.Read(buffer, 0, packetSize)) > 0)
                        {
                            byte[] packet = new byte[bytesRead];
                            Array.Copy(buffer, packet, bytesRead);
                            windowPackets.Add(new UdpPacket(packet));
                        }

                        
                        foreach (var packet in windowPackets)
                        {
                           clientSocket.SendTo(packet.data, serverEndPoint);
                        }
                        //Console.WriteLine("windowPackets.Count:" + windowPackets.Count);
                        
                        if (WaitForAck(windowPackets,waitTime))
                        {
                            
                            currentPacketIndex += windowPackets.Count;
                            windowPackets.Clear();
                        }
                        else
                        {
                            
                            fs.Seek((long)currentPacketIndex * packetSize, SeekOrigin.Begin); 
                            windowPackets.Clear();
                        }

                    }

                    Send("end_file_transfer");
                    Console.WriteLine($"File {fileName} has been sent to the server.");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("\n--------------------------------------------");
                Console.WriteLine("An error occurred while sending the file: " + e.ToString());
                Console.WriteLine("--------------------------------------------");
            }
        }


        private void DownloadFromServer(string download)
        {
            Send(download);
            //canSendToServer = false;
            var startMessage = Read();

            var spitedStart = startMessage.Split(' ');
            if (spitedStart.Length <= 1)
            {
                Console.WriteLine("no start_file_transfer");
                return;
            }
            if (spitedStart[0] != "start_file_transfer")
            {
                Console.WriteLine("no start_file_transfer");
                return;
            }
            int index = 0;
            ulong fileOffset = ulong.Parse(spitedStart[2]);
            string filePath = spitedStart[1];
            string fileName = Path.GetFileName(filePath);
            string fileExtension = Path.GetExtension(filePath);
            Console.Write($"\nStarting to receive {fileName} from the server\n>>");
            var startTime = DateTime.Now;
            FileStream fs = null;
            FileMode filemode = FileMode.Create;
            if (fileOffset > 0)
            {
                filemode = FileMode.OpenOrCreate;
                Console.Write($"\nFile already exist, continue downloading from server\n>>");
            }

            try
            {

                fs = new FileStream($"{fileName}", filemode, FileAccess.Write);

                if (fileOffset != 0)
                {
                    fs.Seek((long)fileOffset, SeekOrigin.Begin);
                }
                while (true)
                {
                    byte[] buffer = new byte[Program.bufferSize];
                    EndPoint senderEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    int receivedBytes = clientSocket.ReceiveFrom(buffer, ref senderEndPoint);
                    string response = Encoding.ASCII.GetString(buffer, 0, receivedBytes);
                    var udpPacket = new UdpPacket(buffer);
                    //Console.WriteLine("ACK " + udpPacket.byteHashCode);
                    byte[] data = Encoding.ASCII.GetBytes("ACK " + udpPacket.byteHashCode);
                    clientSocket.SendTo(data, senderEndPoint);

                    if (response.Contains("end_file_transfer"))
                    {
                        var endTime = DateTime.Now;
                        var deltaTime = endTime - startTime;
                        double bitrate = index / deltaTime.Milliseconds;
                        Console.WriteLine($"File transfer completed. Bitrate: {bitrate / 1024} kb/ms.");
                        break;
                    }

                    index += receivedBytes;
                    fs.Write(buffer, 0, receivedBytes);
                }
            

            }
            catch (SocketException e)
            {
                Console.WriteLine("\n--------------------------------------------");
                Console.WriteLine("SocketException in DownloadFromServer: " + e);
                Console.WriteLine("--------------------------------------------");
            }
            catch (Exception e)
            {
                Console.WriteLine("\n--------------------------------------------");
                Console.WriteLine("An error occurred: " + e.ToString());
                Console.WriteLine("--------------------------------------------");
            }
            finally
            {
                if (fs != null)
                {
                    fs.Close();
                }
            }
        }
    }
}
