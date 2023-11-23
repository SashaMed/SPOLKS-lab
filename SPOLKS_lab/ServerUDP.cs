using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;

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
        private readonly object socketLock = new object();
        private List<EndPoint> needIncomingData = new List<EndPoint>();
        private ConcurrentDictionary<EndPoint, ConcurrentQueue<byte[]>> _incomingData = new ConcurrentDictionary<EndPoint, ConcurrentQueue<byte[]>>();
        private ConcurrentDictionary<EndPoint, TaskCompletionSource<byte[]>> _waiters = new ConcurrentDictionary<EndPoint, TaskCompletionSource<byte[]>>();


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

        public async Task<byte[]> WaitForDataAsync(EndPoint endPoint)
        {
            while (true)
            {
                // Проверяем, есть ли уже данные для этого EndPoint
                if (_incomingData.TryGetValue(endPoint, out var queue) && queue.TryDequeue(out var data))
                {
                    return data;
                }

                // Если данных нет, подготавливаемся к ожиданию
                var tcs = new TaskCompletionSource<byte[]>();
                _waiters[endPoint] = tcs;

                try
                {
                    // Ожидаем, пока данные не появятся
                    return await tcs.Task;
                }
                finally
                {
                    // Удаляем ожидание, когда оно завершено
                    _waiters.TryRemove(endPoint, out var _);
                    if (_incomingData.TryGetValue(endPoint, out var queue1) && queue1.TryDequeue(out var data1))
                    {
                        //Console.WriteLine("Data Dequeued");
                    }
                }
            }
        }

        public void OnDataReceived(byte[] data, EndPoint endPoint)
        {
            // Добавляем данные в очередь
            _incomingData.AddOrUpdate(endPoint, new ConcurrentQueue<byte[]>(), (key, queue) =>
            {
                queue.Enqueue(data);
                return queue;
            });

            // Уведомляем ожидающие задачи, если они есть
            if (_waiters.TryRemove(endPoint, out var tcs))
            {
                tcs.SetResult(data);
            }
        }

        private void ReadUdpPacket()
        {
            try
            {
                byte[] buffer = new byte[Program.bufferSize];
                int receivedBytes = 0;
                string request = "";
                if (serverSocket.Poll(1000, SelectMode.SelectRead))
                {
                    EndPoint clientEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    lock (socketLock)
                    {
                        receivedBytes = serverSocket.ReceiveFrom(buffer, ref clientEndPoint);
                    }
                    if (needIncomingData.Contains(clientEndPoint))
                    {
                        OnDataReceived(buffer, clientEndPoint);
                        //if (!_incomingData.ContainsKey(clientEndPoint))
                        //    _incomingData.TryAdd(clientEndPoint, new ConcurrentQueue<byte[]>());

                        //_incomingData[clientEndPoint].Enqueue(buffer);
                    }
                    else
                    {
                        request = Encoding.ASCII.GetString(buffer, 0, receivedBytes);
                        Console.WriteLine($"Received: {request} from {clientEndPoint}");

                        //CheckIfShouldFinishFileTrasfer(clientEndPoint);

                        var packet = new UdpPacket(request);
                        var stringACK = $"ACK {packet.stringHashCode}";
                        Console.WriteLine("Sent " + stringACK);
                        byte[] data = Encoding.ASCII.GetBytes(stringACK);
                        serverSocket.SendTo(data, clientEndPoint);


                        var packetData = new UdpRequestPacketData
                        {
                            Buffer = buffer,
                            ClientEndPoint = clientEndPoint,
                            ReceivedBytes = receivedBytes
                        };

                        ThreadPool.QueueUserWorkItem(HandlePacket, packetData);
                    }
                }
                //string responseText = HandleCommand(clientEndPoint, request, buffer);
                //if (responseText != "")
                //{
                //    Console.WriteLine("Sent: " + responseText);
                //    Send(responseText, clientEndPoint);
                //}
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

        private async void HandlePacket(object state)
        {
            var packetData = (UdpRequestPacketData)state;
            string request = Encoding.ASCII.GetString(packetData.Buffer, 0, packetData.ReceivedBytes);
            Console.WriteLine($"End point: {packetData.ClientEndPoint}");

            // Остальная логика обработки...
            string responseText = await HandleCommand(packetData.ClientEndPoint, request, packetData.Buffer);
            Console.WriteLine("after HandlePacket");
            if (responseText != "")
            {
                Console.WriteLine("Sent: " + responseText);
                await Send(responseText, packetData.ClientEndPoint);
            }
            Console.WriteLine("HandlePacket ended " + packetData.ClientEndPoint);
            //needIncomingData.Remove(packetData.ClientEndPoint);
        }

        private async Task<string> HandleCommand(EndPoint clientEndPoint, string command, byte[] rawData)
        {
            string result = "no such command";
            var words = command.Split(' ');
            if (words[0] == "ACK")
            {
                return "";
            }
            else if (words[0] == "time")
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
                if (await DownloadFromServer(clientEndPoint, words[1]))
                {
                    result = "";
                }
                else
                {
                    result = "";
                }
            }
            else if (words[0] == "upload" && words.Length > 1)
            {
                await DownloadFromClient(clientEndPoint, words[1], 0);
                result = "";
            }
            else
            {
                result = "";
            }
            return result;
        }


        public async Task Send(string message,EndPoint clientEndPoint, bool ack = false)
        {
            try
            {
                byte[] data = Encoding.ASCII.GetBytes(message);
                lock (socketLock)
                {
                    serverSocket.SendTo(data, clientEndPoint);
                }
                needIncomingData.Add(clientEndPoint);
                //EndPoint ackRecivedEndPoint = new IPEndPoint(IPAddress.Any, 0);
                //var receivedBytes = serverSocket.ReceiveFrom(buffer, ref clientEndPoint);
                var buffer = new byte[Program.bufferSize];
                buffer = await WaitForDataAsync(clientEndPoint);
                var receivedBytes = buffer.Length;
                var response = Encoding.ASCII.GetString(buffer, 0, receivedBytes);
                Console.WriteLine($"Received ack: {response}");
                needIncomingData.Remove(clientEndPoint);
                return;

            }
            catch (SocketException e)
            {
                Console.WriteLine("--------------------------------------------");
                Console.WriteLine("SocketException while sending udp packet: " + e);
                Console.WriteLine("--------------------------------------------");
                return;
            }
        }




        private async Task DownloadFromClient(EndPoint clientEndPoint, string filePath, ulong fileOffset = 0)
        {
            fileOffset = (ulong)CheckIfShouldContinueDownloadFromClient(clientEndPoint, filePath);
            var startMessage = "start_file_transfer " + filePath + " " + fileOffset;
            Console.WriteLine($"Sent: {startMessage}");
            await Send(startMessage, clientEndPoint);

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
                //originalTimeout = (int)serverSocket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout);
                //const int timeout = 5000;
                //serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, timeout);
                fs = new FileStream($"{fileName}", filemode, FileAccess.Write);
                if (fileOffset != 0)
                {
                    fs.Seek((long)fileOffset, SeekOrigin.Begin);
                }
                needIncomingData.Add(clientEndPoint);
                while (true)
                {
                    byte[] buffer = new byte[Program.bufferSize];
                    EndPoint senderEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    int receivedBytes = 0;
                    var response = "";
                    buffer = await WaitForDataAsync(clientEndPoint);
                    receivedBytes = buffer.Length;
                    response = Encoding.ASCII.GetString(buffer, 0, receivedBytes);
                    //receivedBytes = serverSocket.ReceiveFrom(buffer, ref senderEndPoint);
                    //Console.WriteLine($"Received data: {response[0..100]}");
                    //Console.WriteLine("ACK " + udpPacket.byteHashCode);
                    var udpPacket = new UdpPacket(buffer);
                    byte[] data = Encoding.ASCII.GetBytes("ACK " + udpPacket.byteHashCode);
                    lock (socketLock)
                    {
                        serverSocket.SendTo(data, clientEndPoint);
                    }


                    if (response.Contains("end_file_transfer"))
                    {
                        var endTime = DateTime.Now;
                        var deltaTime = endTime - startTime;
                        double bitrate = (long)index / deltaTime.Milliseconds;
                        Console.WriteLine($"File transfer completed. Bitrate: {bitrate / 1024} kb/ms.");
                        Console.WriteLine($"queque.Count = {_incomingData[clientEndPoint].Count}");
                        needIncomingData.Remove(clientEndPoint);
                        ClearDataForEndPoint(clientEndPoint);
                        break;
                    }

                    index += (ulong)receivedBytes;
                    fs.Write(buffer, 0, receivedBytes);
                }

            }
            catch (SocketException e)
            {
                needIncomingData.Remove(clientEndPoint);
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

        public void ClearDataForEndPoint(EndPoint endPoint)
        {
            // Удаление всех данных для EndPoint из _incomingData
            _incomingData.TryRemove(endPoint, out var _);

            // Удаление TaskCompletionSource из _waiters, если он существует для этого EndPoint
            if (_waiters.TryRemove(endPoint, out var tcs))
            {
                // Можно также установить результат или исключение для TaskCompletionSource,
                // чтобы уведомить ожидающие задачи о том, что данные больше не будут доступны.
                tcs.SetCanceled(); // или tcs.SetException(new SomeException());
            }
        }

        private async Task<bool> WaitForAck(EndPoint endPoint, List<UdpPacket> windowPackets, int waitTime)
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
                    //byte[] buffer = new byte[Program.bufferSize];
                    //serverSocket.ReceiveFrom(buffer, ref clientEndPoint);
                    byte[] buffer = await WaitForDataAsync(endPoint);
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

        private async Task<bool> DownloadFromServer(EndPoint clientEndPoint, string fileName, long filePosition = 0)
        {
            //IPEndPoint clientEndPoint = clientSocket.RemoteEndPoint as IPEndPoint;
            long index = 0;
            if (!File.Exists(fileName))
            {
                string message = "File does not exist";
                //byte[] errorMsg = Encoding.ASCII.GetBytes(message);
                await Send(message, clientEndPoint);
                Console.WriteLine($"Client requested a file that does not exist: {fileName}");
                return false;
            }
            try
            {
                
                //originalTimeout = (int)serverSocket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout);

                filePosition = CheckIfShouldContinueDownloadFromServer(clientEndPoint, fileName);
                index = filePosition;
                Console.WriteLine($"Sending {fileName} file to the client.");
                Console.WriteLine($"start_file_transfer {fileName} {filePosition}");
                await Send($"start_file_transfer {fileName} {filePosition}", clientEndPoint);
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
                    needIncomingData.Add(clientEndPoint);
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

                        if (await WaitForAck(clientEndPoint, windowPackets, waitTime))
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
                    needIncomingData.Remove(clientEndPoint);
                    ClearDataForEndPoint(clientEndPoint);
                    await Send("end_file_transfer", clientEndPoint);
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

