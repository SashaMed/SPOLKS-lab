﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace SPOLKS_lab
{
    internal class ServerUDP
    {
        private Socket serverSocket;
        IPEndPoint serverLocalEndPoint;
        private List<Socket> clientSockets = new List<Socket>();
        private List<ClientData> unconnectedClientDatas = new List<ClientData>();
        private Dictionary<string, ClientData> clientsProgress;
        private List<UdpPacket> packets;

        private EndPoint clientEndPoint;
        private int originalTimeout;

        public ServerUDP(string ipAddress, int port)
        {
            packets = new List<UdpPacket>();
            IPAddress localAddr = IPAddress.Parse(ipAddress);
            serverLocalEndPoint = new IPEndPoint(localAddr, port);

            serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            serverSocket.Bind(serverLocalEndPoint);
            clientsProgress = new Dictionary<string, ClientData>();
        }

        public void Start()
        {
            try
            {
                Console.WriteLine($"The upd server is running on {serverLocalEndPoint.Address}:{serverLocalEndPoint.Port}." +
                    $" Waiting for connections...");
                while (true)
                {
                    ReadUdpPacket();
                }
            }
            catch (SocketException e)
            {
                Console.WriteLine("--------------------------------------------");
                Console.WriteLine("SocketException: " + e);
                Console.WriteLine("--------------------------------------------");
            }
            finally
            {
                serverSocket.Close();
            }
        }

        private void ReadUdpPacket()
        {
            try
            {
                byte[] buffer = new byte[Program.bufferSize];
                EndPoint clientEndPoint = new IPEndPoint(IPAddress.Any, 0);
                int receivedBytes = serverSocket.ReceiveFrom(buffer, ref clientEndPoint);
                string response = Encoding.ASCII.GetString(buffer, 0, receivedBytes);
                Console.WriteLine($"Received: {response} from {clientEndPoint}");
                //CheckIfShouldFinishFileTrasfer(clientEndPoint);

                var packet = new UdpPacket(response);
                var stringACK = $"ACK {packet.stringHashCode}";
                Console.WriteLine("Sent " + stringACK);
                byte[] data = Encoding.ASCII.GetBytes(stringACK);
                serverSocket.SendTo(data, clientEndPoint);

                string responseText = HandleCommand(serverSocket, clientEndPoint, response, buffer);
                if (responseText != "")
                {
                    Console.WriteLine("Sent: " + responseText);
                    Send(responseText, clientEndPoint);
                }
            }
            catch (SocketException e)
            {
                if (e.SocketErrorCode == SocketError.TimedOut)
                {
                    // Обработка таймаута
                    Console.WriteLine("Timeout occurred, continuing to listen...");
                    serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, originalTimeout);
                }
                else
                {
                    Console.WriteLine("--------------------------------------------");
                    Console.WriteLine("SocketException at ReadUdpPacket: " + e);
                    Console.WriteLine("--------------------------------------------");
                }
            }

        }

        private string HandleCommand(Socket clientSocket, EndPoint clientEndPoint, string command, byte[] rawData)
        {
            string result = "no such command";
            //Console.Write("\nRaw data length " + rawData.Length + "\n");
            //var incomingPacket = new UdpPacket(command);
            //command = command.ToLower();
            //Console.WriteLine("HandleCommand:" + command + ";");
            var words = command.Split(' ');
            //if (words[0] == "ACK")
            //{
            //    ReceivedACK(command);
            //    return "";
            //}
            if (words[0] == "time")
            {
                result = DateTime.Now.ToString();
            }
            else if (words[0] == "echo")
            {
                if (words.Length > 1)
                {
                    result = string.Join(" ", words, 1, words.Length - 1);
                }
                else
                {
                    result = "echo";
                }
            }
            else if (words[0] == "download" && words.Length > 1)
            {
                if (DownloadFromServer(clientEndPoint, words[1]))
                {
                    result = "download complete";
                }
                else
                {
                    result = "";
                }
            }
            else if (words[0] == "upload" && words.Length > 1)
            {
                //var index = ulong.Parse(words[2]);
                DownloadFromClient(clientEndPoint, words[1], 0);
                result = "";
            }
            else
            {
                result = "";
            }
            //Console.Write($"\nBefore sending ack\n");
            //SendACK(clientEndPoint, incomingPacket.stringHashCode);
            return result;
        }




        private void ReceivedACK(string ack)
        {
            var splitedAck = ack.Split(" ");
            if (splitedAck.Length > 1)
            {
                var res = packets.Where(c => c.stringHashCode == int.Parse(splitedAck[1])).FirstOrDefault();
                if (res != null)
                {
                    Console.Write($"Received: {ack}\nRemove packet from list\n");
                    packets.Remove(res);
                }
                else
                {
                    Console.Write($"Received: {ack}\nPacket not found, {packets.Count}\n");
                    foreach (var pack in packets)
                    {
                        Console.WriteLine(pack.stringHashCode.ToString());
                    }
                }
            }
            else
            {
                Console.Write($"Received: {ack}\n");
            }
        }



        public void Send(string message,EndPoint clientEndPoint, bool ack = false)
        {
            try
            {
                byte[] data = Encoding.ASCII.GetBytes(message);
                serverSocket.SendTo(data, clientEndPoint);

                EndPoint ackRecivedEndPoint = new IPEndPoint(IPAddress.Any, 0);
                var buffer = new byte[Program.bufferSize];
                var receivedBytes = serverSocket.ReceiveFrom(buffer, ref ackRecivedEndPoint);
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




        private void DownloadFromClient(EndPoint clientEndPoint, string filePath, ulong fileOffset = 0)
        {
            fileOffset = (ulong)CheckIfShouldContinueDownloadFromClient(clientEndPoint,filePath);
            var startMessage = "start_file_transfer " + filePath + " " + fileOffset;
            Console.WriteLine($"Sent: {startMessage}");
            Send(startMessage, clientEndPoint);
            ulong index = fileOffset;
            string fileName = Path.GetFileName(filePath);
            var startTime = DateTime.Now;
            FileStream fs = null;
            Console.WriteLine($"Starting to receive {fileName} from the client");
            FileMode filemode = FileMode.Create;
            if (fileOffset > 0)
            {
                filemode = FileMode.OpenOrCreate;
                Console.WriteLine($"File already exist, continue downloading from client");
            }

            try
            {
                originalTimeout = (int)serverSocket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout);
                const int timeout = 5000;
                serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, timeout);
                fs = new FileStream($"{fileName}", filemode, FileAccess.Write);
                if (fileOffset != 0)
                {
                    fs.Seek((long)fileOffset, SeekOrigin.Begin);
                }

                while (true)
                {
                    byte[] buffer = new byte[Program.bufferSize];
                    EndPoint senderEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    int receivedBytes = serverSocket.ReceiveFrom(buffer, ref senderEndPoint);
                    string response = Encoding.ASCII.GetString(buffer, 0, receivedBytes);
                    var udpPacket = new UdpPacket(buffer);
                    //Console.WriteLine("ACK " + udpPacket.byteHashCode);
                    byte[] data = Encoding.ASCII.GetBytes("ACK " + udpPacket.byteHashCode);
                    serverSocket.SendTo(data, clientEndPoint);

                    if (response.Contains("end_file_transfer"))
                    {
                        var endTime = DateTime.Now;
                        var deltaTime = endTime - startTime;
                        double bitrate = (long)index / deltaTime.Milliseconds;
                        Console.WriteLine($"File transfer completed. Bitrate: {bitrate / 1024} kb/s.");
                        break;
                    }

                    index += (ulong)receivedBytes;
                    fs.Write(buffer, 0, receivedBytes);
                }

            }
            catch (SocketException e)
            {
                var client = new ClientData();
                client.mode = "upload";
                client.endPoint = clientEndPoint;
                client.index = (long)index;
                client.filename = filePath;
                clientsProgress.TryAdd($"{clientEndPoint}", client);
                Console.WriteLine("--------------------------------------------");
                Console.WriteLine("SocketException in DownloadFromClient: " + e);
                Console.WriteLine("--------------------------------------------");
            }
            catch (Exception e)
            {
                Console.WriteLine("--------------------------------------------");
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

        private bool WaitForAck(List<UdpPacket> windowPackets, int waitTime)
        {
            int ackCount = 0;
            int expectedAcks = windowPackets.Count;
            const int timeout = 5000;

            serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, timeout);

            try
            {
                for (int i = 0; i < expectedAcks; i++)
                {
                    EndPoint clientEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    byte[] buffer = new byte[Program.bufferSize];
                    serverSocket.ReceiveFrom(buffer, ref clientEndPoint);

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

        private bool DownloadFromServer(EndPoint clientEndPoint, string fileName, long filePosition = 0)
        {
            //IPEndPoint clientEndPoint = clientSocket.RemoteEndPoint as IPEndPoint;
            long index = 0;
            if (!File.Exists(fileName))
            {
                string message = "File does not exist";
                //byte[] errorMsg = Encoding.ASCII.GetBytes(message);
                Send(message, clientEndPoint);
                Console.WriteLine($"Client requested a file that does not exist: {fileName}");
                return false;
            }
            try
            {
                
                originalTimeout = (int)serverSocket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout);

                filePosition = CheckIfShouldContinueDownloadFromServer(clientEndPoint, fileName);
                index = filePosition;
                Console.WriteLine($"Sending {fileName} file to the client.");
                Console.WriteLine($"start_file_transfer {fileName} {filePosition}");
                Send($"start_file_transfer {fileName} {filePosition}", clientEndPoint);
                const int windowSize = 5;
                int packetSize = Program.bufferSize;
                int waitTime = 5;
                List<UdpPacket> windowPackets = new List<UdpPacket>();
                int currentPacketIndex = 0;

                using (FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read))
                {
                    if (filePosition != 0)
                    {
                        fs.Seek((long)filePosition, SeekOrigin.Begin);
                    }
                    currentPacketIndex = (int)(filePosition / (long)packetSize);

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
                            index += packetSize;
                        }


                        foreach (var packet in windowPackets)
                        {
                            serverSocket.SendTo(packet.data, clientEndPoint);
                        }
                        //Console.WriteLine("windowPackets.Count:" + windowPackets.Count);

                        if (WaitForAck(windowPackets, waitTime))
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

                    Send("end_file_transfer", clientEndPoint);
                    Console.WriteLine($"File {fileName} has been sent to the server.");

                }
            }
            catch (Exception e)
            {
                var client = new ClientData();
                client.mode = "download";
                client.endPoint = clientEndPoint;
                client.index = index;
                client.filename = fileName;
                clientsProgress.TryAdd($"{clientEndPoint}", client);
                Console.WriteLine("--------------------------------------------");
                Console.WriteLine("An error occurred while sending the file: " + e.ToString());
                Console.WriteLine("--------------------------------------------");
                return false;
            }
            return true;
        }

        private void HandleConsoleInput()
        {
            while (true)
            {
                string command = Console.ReadLine();
                if (command.ToLower().Trim() == "close")
                {
                    foreach (var clientSocket in clientSockets)
                    {
                        CloseConnectionWithClient(clientSocket);
                    }
                    serverSocket.Close();
                    Environment.Exit(0);
                }
            }
        }

        private void CloseConnectionWithClient(Socket clientSocket)
        {
            IPEndPoint clientEndPoint = clientSocket.RemoteEndPoint as IPEndPoint;
            Console.WriteLine($"Closing connection for {clientEndPoint.Address}:{clientEndPoint.Port}");
            byte[] closeMessage = Encoding.ASCII.GetBytes("closing connection");
            clientSocket.Send(closeMessage);
        }

        private long CheckIfShouldContinueDownloadFromServer(EndPoint clientEndPoint, string filename)
        {
            var clientConnectionInfo = $"{clientEndPoint}";
            if (clientsProgress.TryGetValue(clientConnectionInfo, out var clientData))
            {

                if (clientData.mode == "download" && clientData.filename == filename)
                {
                    Console.WriteLine($"The client {clientConnectionInfo} has connected again," +
    $" client has unfinished operation: {clientData.mode}");
                    //DownloadFromServer(clientEndPoint, clientData.filename, clientData.index);
                    Console.WriteLine($"ClientData.index: {clientData.index}");
                    clientsProgress.Remove(clientConnectionInfo);
                    return clientData.index;
                }
            }
            return 0;
        }


        private long CheckIfShouldContinueDownloadFromClient(EndPoint clientEndPoint, string filename)
        {
            var clientConnectionInfo = $"{clientEndPoint}";
            if (clientsProgress.TryGetValue(clientConnectionInfo, out var clientData))
            {

                if (clientData.mode == "upload" && clientData.filename == filename)
                {
                    Console.WriteLine($"The client {clientConnectionInfo} has connected again," +
    $" client has unfinished operation: {clientData.mode}");
                    //DownloadFromServer(clientEndPoint, clientData.filename, clientData.index);
                    Console.WriteLine($"ClientData.index: {clientData.index}");
                    clientsProgress.Remove(clientConnectionInfo);
                    return clientData.index;
                }
            }
            return 0;
        }

        //private void SendACK(EndPoint clientEndPoint, int hashCode)
        //{
        //    try
        //    {
        //        var stringACK = $"ACK {hashCode}";
        //        Console.WriteLine("Sent " + stringACK);
        //        Send(stringACK, clientEndPoint, true);
        //    }
        //    catch (SocketException e)
        //    {
        //        Console.WriteLine("--------------------------------------------");
        //        Console.WriteLine("SocketException while sending ack: " + e);
        //        Console.WriteLine("--------------------------------------------");
        //    }
        //}


        private void CheckIfShouldFinishFileTrasfer(EndPoint clientEndPoint)
        {
            //var clientEndPoint = clientSocket.RemoteEndPoint as IPEndPoint;
            var clientConnectionInfo = $"{clientEndPoint}";
            if (clientsProgress.TryGetValue(clientConnectionInfo, out var clientData))
            {
                Console.WriteLine($"The client {clientConnectionInfo} has connected again," +
                    $" client has unfinished operation: {clientData.mode}");
                if (clientData.mode == "download")
                {
                    DownloadFromServer(clientEndPoint, clientData.filename, clientData.index);
                    Console.WriteLine($"Client {clientConnectionInfo} operation is finished: {clientData.mode}");
                    clientsProgress.Remove(clientConnectionInfo);
                }
                else if (clientData.mode == "upload")
                {
                    //SendRequestForUploadContinue(clientSocket, clientData.filename, clientData.index);
                    Console.WriteLine($"Request for upload continue was sent the client {clientConnectionInfo}");
                    clientsProgress.Remove(clientConnectionInfo);
                }
            }
        }

        private void SendRequestForUploadContinue(Socket clientSocket, string filename, long index)
        {
            try
            {
                var request = $"request_for_upload {filename} {index}";
                byte[] response = Encoding.ASCII.GetBytes(request);
                clientSocket.Send(response);
            }
            catch (SocketException e)
            {
                Console.WriteLine("--------------------------------------------");
                Console.WriteLine("SocketException at Request for Upload: " + e);
                Console.WriteLine("--------------------------------------------");
            }

        }

        //private void HandleClient(Socket clientSocket)
        //{
        //    byte[] buffer = new byte[Program.bufferSize];
        //    int bytesRead;

        //    IPEndPoint clientEndPoint = clientSocket.RemoteEndPoint as IPEndPoint;
        //    try
        //    {
        //        while ((bytesRead = clientSocket.Receive(buffer)) > 0)
        //        {
        //            string data = Encoding.ASCII.GetString(buffer, 0, bytesRead);
        //            var recievedPacket = new UdpPacket(buffer);
        //            if (data.Trim().ToLower() == "close")
        //            {
        //                CloseConnectionWithClient(clientSocket);
        //                break;
        //            }
        //            Console.WriteLine($"Received from {clientEndPoint.Address}:{clientEndPoint.Port}: {data}");
        //            //SendACK(clientSocket, recievedPacket.hashCode);
        //            var stringResponse = HandleCommand(clientSocket, data);
        //            Console.WriteLine($"Response: {stringResponse}");

        //            byte[] response = Encoding.ASCII.GetBytes(stringResponse);
        //            clientSocket.Send(response);
        //        }
        //    }
        //    catch (SocketException e)
        //    {
        //        Console.WriteLine("--------------------------------------------");
        //        Console.WriteLine("SocketException: " + e);
        //        Console.WriteLine("--------------------------------------------");
        //    }

        //    clientSocket.Close();
        //}
    }
}

