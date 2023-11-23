using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.IO;
using System.Data;

namespace SPOLKS_lab
{
    internal class Server
    {
        private Socket serverSocket;
        IPEndPoint serverLocalEndPoint;
        private List<Socket> clientSockets = new List<Socket>();
        private List<ClientData> unconnectedClientDatas = new List<ClientData>();
        private Dictionary<string, ClientData> clientsProgress;

        private Dictionary<Socket, FileData> uploadFromClientIndices = new Dictionary<Socket, FileData>();
        private Dictionary<Socket, FileData> downloadFromServerIndices = new Dictionary<Socket, FileData>();


        public Server(string ipAddress, int port)
        {
            IPAddress localAddr = IPAddress.Parse(ipAddress);
            serverLocalEndPoint = new IPEndPoint(localAddr, port);

            serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            serverSocket.Bind(serverLocalEndPoint);
            clientsProgress = new Dictionary<string, ClientData>();
        }

        //public void Start()
        //{
        //    try
        //    {
        //        serverSocket.Listen(10);
        //        Console.WriteLine($"The tcp server is running on {serverLocalEndPoint.Address}:{serverLocalEndPoint.Port}. Waiting for connections...");

        //        Thread commandThread = new Thread(HandleConsoleInput);
        //        commandThread.Start();

        //        while (true)
        //        {
        //            Socket clientSocket = serverSocket.Accept();
        //            clientSockets.Add(clientSocket);
        //            IPEndPoint clientEndPoint = clientSocket.RemoteEndPoint as IPEndPoint;
        //            Console.WriteLine("Client connected. IP: {0}, Port: {1}", clientEndPoint.Address, clientEndPoint.Port);
        //            Thread clientThread = new System.Threading.Thread(() =>
        //            {
        //                HandleClient(clientSocket);
        //            });
        //            clientThread.Start();
        //        }
        //    }
        //    catch (SocketException e)
        //    {
        //        Console.WriteLine("--------------------------------------------");
        //        Console.WriteLine("SocketException: " + e);
        //        Console.WriteLine("--------------------------------------------");
        //    }
        //    finally
        //    {
        //        serverSocket.Close();
        //    }
        //}



        public void Start()
        {
            try
            {
                serverSocket.Listen(10);
                Console.WriteLine($"The tcp server is running on {serverLocalEndPoint.Address}:{serverLocalEndPoint.Port}. Waiting for connections...");

                while (true)
                {
                    List<Socket> checkRead = new List<Socket> { serverSocket };
                    checkRead.AddRange(clientSockets);
                    List<Socket> checkWrite = new List<Socket>();
                    checkWrite.AddRange(clientSockets);


                    Socket.Select(checkRead, checkWrite, null, 10000);

                    foreach (Socket socket in checkRead)
                    {
                        if (socket == serverSocket)
                        {

                            Socket clientSocket = serverSocket.Accept();
                            clientSockets.Add(clientSocket);
                            IPEndPoint clientEndPoint = clientSocket.RemoteEndPoint as IPEndPoint;
                            Console.WriteLine("Client connected. IP: {0}, Port: {1}", clientEndPoint.Address, clientEndPoint.Port);
                        }
                        else
                        {
                            HandleRequest(socket);
                        }
                    }
                    var filedata = new FileData();
                    foreach (Socket socket in checkWrite)
                    {
                        if (downloadFromServerIndices.TryGetValue(socket, out filedata))
                        {
                            DownloadFromServer(socket, filedata);
                            
                        }
                    }
                    
                }
            }
            catch (SocketException e)
            {
                Console.WriteLine("--------------------------------------------");
                Console.WriteLine("SocketException at start: " + e);
                Console.WriteLine("--------------------------------------------");
            }
            catch (Exception e)
            {
                Console.WriteLine("--------------------------------------------");
                Console.WriteLine("Exception at start: " + e);
                Console.WriteLine("--------------------------------------------");
            }
            finally
            {
                serverSocket.Close();
            }
        }

        private void HandleRequest(Socket clientSocket)
        {
            byte[] buffer = new byte[Program.bufferSize];
            int bytesRead;
            var filedata = new FileData();
            IPEndPoint clientEndPoint = clientSocket.RemoteEndPoint as IPEndPoint;
            try
            {
                if (uploadFromClientIndices.TryGetValue(clientSocket, out filedata))
                {
                    DownloadFromClient(clientSocket, filedata);
                    return;
                }
                //string data = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                string data = Read(clientSocket);
                if (data == "error")
                {
                    return;
                }
                if (data.Trim().ToLower() == "close")
                {
                    CloseConnectionWithClient(clientSocket);
                    return;
                }
                Console.WriteLine($"Received from {clientEndPoint.Address}:{clientEndPoint.Port}: {data}");
                var stringResponse = HandleCommand(clientSocket, data);
                if (stringResponse != "")
                {
                    Console.WriteLine($"Response: {stringResponse}");
                    Send(clientSocket, stringResponse);
                }

            }
            catch (SocketException e)
            {
                Console.WriteLine("--------------------------------------------");
                Console.WriteLine("SocketException: " + e);
                Console.WriteLine("--------------------------------------------");
            }

            //clientSocket.Close();
        }

        private string HandleCommand(Socket clientSocket, string command)
        {
            string result = "no such command";
            command = command.ToLower();
            var words = command.Split(' ');
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
                StartDownloadFromServer(clientSocket, words[1]);
                result = "";
            }
            else if (words[0] == "upload" && words.Length > 1)
            {
                //var index = ulong.Parse(words[2]);
                StartDownloadFromClient(clientSocket, words[1]);
                result = "";
            }

            return result;
        }

        public void StartDownloadFromClient(Socket clientSocket, string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            FileStream fs = null;
            IPEndPoint clientEndPoint = clientSocket.RemoteEndPoint as IPEndPoint;
            int index = 0;
            ulong fileOffset = (ulong)CheckIfShouldContinueDownloadFromClient(clientSocket, filePath);
            var startMessage = "start_file_transfer " + filePath + " " + fileOffset;
            Console.WriteLine($"Sent: {startMessage}");
            Send(clientSocket, startMessage);
            Console.WriteLine($"Starting to receive {fileName} from the server");
            FileMode filemode = FileMode.Create;
            if (fileOffset > 0)
            {
                filemode = FileMode.OpenOrCreate;
                Console.WriteLine($"File already exist, continue downloading from client");
            }

            var startTime = DateTime.Now;
            fs = new FileStream($"{fileName}", filemode, FileAccess.Write);
            var fileData = new FileData();
            fileData.index = fileOffset;
            fileData.fileStream = fs;
            fileData.startTime = startTime;
            fileData.filename= fileName;
            uploadFromClientIndices.Add(clientSocket, fileData);
            DownloadFromClient(clientSocket, fileData);

        }

        private void DownloadFromClient(Socket clientSocket, FileData fileData)
        {
            IPEndPoint clientEndPoint = clientSocket.RemoteEndPoint as IPEndPoint;

            try
            {
                if (fileData.index != 0)
                {
                    fileData.fileStream.Seek((long)fileData.index, SeekOrigin.Begin);
                }

                byte[] buffer = new byte[Program.bufferSize];
                int bytesRead;
                bytesRead = clientSocket.Receive(buffer);
                fileData.index += (ulong)Program.bufferSize;
                string response = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                if (response.Contains("end_file_transfer"))
                {
                    var endTime = DateTime.Now;
                    var deltaTime = endTime - fileData.startTime;
                    var bitrate = fileData.index / (ulong)deltaTime.Seconds;
                    Console.WriteLine($"File transfer completed. Bitrate: {bitrate / 1024} kb/s.");
                    if (fileData.fileStream != null)
                    {
                        fileData.fileStream.Close();
                    }
                    uploadFromClientIndices.Remove(clientSocket);
                    return;

                }
                fileData.fileStream.Write(buffer, 0, bytesRead);
                uploadFromClientIndices[clientSocket] = fileData;
                return;

            }
            catch (SocketException e)
            {
                var client = new ClientData();
                client.clientSocket = clientSocket;
                client.mode = "upload";
                client.port = clientEndPoint.Port;
                client.ipAddres = clientEndPoint.Address.ToString();
                client.index = (long)fileData.index;
                client.filename = fileData.filename;
                uploadFromClientIndices.Remove(clientSocket);
                clientsProgress.TryAdd($"{clientEndPoint.Address.ToString()}:{clientEndPoint.Port}", client);
                unconnectedClientDatas.Add(client);
                Console.WriteLine("--------------------------------------------");
                Console.WriteLine("SocketException in DownloadFromClient: " + e);
                Console.WriteLine("--------------------------------------------");
            }
            catch (Exception e)
            {
                Console.WriteLine("--------------------------------------------");
                Console.WriteLine("An error occurred: " + e.ToString());
                Console.WriteLine("--------------------------------------------");
                if (fileData.fileStream != null)
                {
                    fileData.fileStream.Close();
                }
            }
            //finally
            //{
            //    if (fileData.fileStream != null)
            //    {
            //        fileData.fileStream.Close();
            //    }
            //}
        }

        private string Read(Socket clientSocket)
        {
            try
            {
                byte[] buffer = new byte[Program.bufferSize];
                int bytesRead;
                bytesRead = clientSocket.Receive(buffer);
                var response = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                return response;
            }
            catch (Exception e)
            {
                clientSocket.Close();
                Console.WriteLine("SocketException in Read: " + e);
                Console.WriteLine("--------------------------------------------");
                return "error";
            }
        }


        private void Send(Socket clientSocket, string message)
        {
            try
            {
                byte[] data = Encoding.ASCII.GetBytes(message);
                clientSocket.Send(data);
            }
            catch (Exception e)
            {
                Console.WriteLine("--------------------------------------------");
                Console.WriteLine("SocketException in Send: " + e);
                Console.WriteLine("--------------------------------------------");
            }
        }

        private void StartDownloadFromServer(Socket clientSocket, string fileName)
        {
            try
            {
                IPEndPoint clientEndPoint = clientSocket.RemoteEndPoint as IPEndPoint;
                long index = 0;
                if (!File.Exists(fileName))
                {
                    string message = "File does not exist";
                    byte[] errorMsg = Encoding.ASCII.GetBytes(message);
                    clientSocket.Send(errorMsg);
                    Console.WriteLine($"Client requested a file that does not exist: {fileName}");
                    return;
                }

                long filePosition = CheckIfShouldContinueDownloadFromServer(clientSocket, fileName);
                var downloadMsg = $"start_file_transfer {fileName} {filePosition}";
                Console.WriteLine(downloadMsg);
                Send(clientSocket, downloadMsg);
                Console.WriteLine($"Sending {fileName} file to the client.");

                var startTime = DateTime.Now;
                FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read);
                var fileData = new FileData();
                fileData.index = (ulong)filePosition;
                fileData.fileStream = fs;
                fileData.startTime = startTime;
                fileData.filename = fileName;
                downloadFromServerIndices.Add(clientSocket, fileData);
                DownloadFromServer(clientSocket, fileData);
            }
            catch(Exception ex)
            {
                Console.WriteLine("--------------------------------------------");
                Console.WriteLine("An error occurred at StartDownloadFromServer: " + ex.ToString());
                Console.WriteLine("--------------------------------------------");
            }
        }

        private void DownloadFromServer(Socket clientSocket, FileData fileData)
        {
            IPEndPoint clientEndPoint = clientSocket.RemoteEndPoint as IPEndPoint;
            try
            {
                fileData.fileStream.Seek((long)fileData.index, SeekOrigin.Begin);
                byte[] buffer = new byte[Program.bufferSize];
                int bytesRead;
                bytesRead = fileData.fileStream.Read(buffer, 0, buffer.Length);
                fileData.index += (ulong)Program.bufferSize;
                clientSocket.Send(buffer, 0, bytesRead, SocketFlags.None);
                downloadFromServerIndices[clientSocket] = fileData;
                if (bytesRead < Program.bufferSize)
                {
                    byte[] endMsg = Encoding.ASCII.GetBytes("end_file_transfer");
                    clientSocket.Send(endMsg);
                    Console.WriteLine($"File {fileData.filename} has been sent to the client.");
                    if (fileData.fileStream != null)
                    {
                        fileData.fileStream.Close();
                    }
                    downloadFromServerIndices.Remove(clientSocket);
                    return;
                }

            }
            catch (Exception e)
            {
                var client = new ClientData();
                client.clientSocket = clientSocket;
                client.mode = "download";
                client.port = clientEndPoint.Port;
                client.ipAddres = clientEndPoint.Address.ToString();
                client.index = (long)fileData.index;
                client.filename = fileData.filename;
                clientsProgress.TryAdd($"{clientEndPoint.Address.ToString()}:{clientEndPoint.Port}", client);
                unconnectedClientDatas.Add(client);
                if (fileData.fileStream != null)
                {
                    fileData.fileStream.Close();
                }
                downloadFromServerIndices.Remove(clientSocket);
                Console.WriteLine("--------------------------------------------");
                Console.WriteLine("An error occurred while sending the file: " + e.ToString());
                Console.WriteLine("--------------------------------------------");
            }
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


        private long CheckIfShouldContinueDownloadFromServer(Socket clientSocket, string filename)
        {
            //
            //var clientConnectionInfo = $"{clientEndPoint}";

            var clientEndPoint = clientSocket.RemoteEndPoint as IPEndPoint;
            var clientConnectionInfo = $"{clientEndPoint.Address.ToString()}:{clientEndPoint.Port}";
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


        private long CheckIfShouldContinueDownloadFromClient(Socket clientSocket, string filename)
        {
            var clientEndPoint = clientSocket.RemoteEndPoint as IPEndPoint;
            var clientConnectionInfo = $"{clientEndPoint.Address.ToString()}:{clientEndPoint.Port}";
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
    }
}
