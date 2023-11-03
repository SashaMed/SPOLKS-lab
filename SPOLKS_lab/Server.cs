using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.IO;

namespace SPOLKS_lab
{
    internal class Server
    {
        private Socket serverSocket;
        IPEndPoint serverLocalEndPoint;
        private List<Socket> clientSockets = new List<Socket>();
        private List<ClientData> unconnectedClientDatas = new List<ClientData>();
        private Dictionary<string, ClientData> clientsProgress;


        public Server(string ipAddress, int port)
        {
            IPAddress localAddr = IPAddress.Parse(ipAddress);
            serverLocalEndPoint = new IPEndPoint(localAddr, port);

            serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            serverSocket.Bind(serverLocalEndPoint);
            clientsProgress = new Dictionary<string, ClientData>();
        }

        public void Start()
        {
            try
            {
                serverSocket.Listen(10);
                Console.WriteLine($"The server is running on {serverLocalEndPoint.Address}:{serverLocalEndPoint.Port}. Waiting for connections...");

                Thread commandThread = new Thread(HandleConsoleInput);
                commandThread.Start();

                while (true)
                {
                    Socket clientSocket = serverSocket.Accept();
                    clientSockets.Add(clientSocket);
                    IPEndPoint clientEndPoint = clientSocket.RemoteEndPoint as IPEndPoint;
                    Console.WriteLine("Client connected. IP: {0}, Port: {1}", clientEndPoint.Address, clientEndPoint.Port);
                    CheckIfShouldFinishFileTrasfer(clientSocket);
                    System.Threading.Thread clientThread = new System.Threading.Thread(() =>
                    {
                        HandleClient(clientSocket);
                    });
                    clientThread.Start();
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


        private void CheckIfShouldFinishFileTrasfer(Socket clientSocket)
        {
            var clientEndPoint = clientSocket.RemoteEndPoint as IPEndPoint;
            var clientConnectionInfo = $"{clientEndPoint.Address.ToString()}:{clientEndPoint.Port}";
            if (clientsProgress.TryGetValue(clientConnectionInfo, out var clientData))
            {
                Console.WriteLine($"The client {clientConnectionInfo} has connected again," +
                    $" client has unfinished operation: {clientData.mode}");
                if (clientData.mode == "download")
                {
                    DownloadFromServer(clientSocket, clientData.filename, clientData.index);
                    Console.WriteLine($"Client {clientConnectionInfo} operation is finished: {clientData.mode}");
                    clientsProgress.Remove(clientConnectionInfo);
                }
                else if (clientData.mode == "upload")
                {
                    SendRequestForUploadContinue(clientSocket, clientData.filename, clientData.index);
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

        private void HandleClient(Socket clientSocket)
        {
            byte[] buffer = new byte[Program.bufferSize];
            int bytesRead;

            IPEndPoint clientEndPoint = clientSocket.RemoteEndPoint as IPEndPoint;
            try
            {
                while ((bytesRead = clientSocket.Receive(buffer)) > 0)
                {
                    string data = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    if (data.Trim().ToLower() == "close")
                    {
                        CloseConnectionWithClient(clientSocket);
                        break; 
                    }
                    Console.WriteLine($"Received from {clientEndPoint.Address}:{clientEndPoint.Port}: {data}");
                    var stringResponse = HandleCommand(clientSocket, data);
                    Console.WriteLine($"Response: {stringResponse}");

                    byte[] response = Encoding.ASCII.GetBytes(stringResponse);
                    clientSocket.Send(response);
                }
            }
            catch(SocketException e)
            {
                Console.WriteLine("--------------------------------------------");
                Console.WriteLine("SocketException: " + e);
                Console.WriteLine("--------------------------------------------");
            }

            clientSocket.Close();
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
                DownloadFromServer(clientSocket, words[1]);
                result = "download complete";
            }
            else if (words[0] == "start_file_transfer" && words.Length > 1)
            {
                var index = ulong.Parse(words[2]);
                DownloadFromClient(clientSocket, words[1], index);
                result = "file uploaded from client";
            }

            return result;
        }


        private void DownloadFromClient(Socket clientSocket, string filePath, ulong fileOffset = 0)
        {
            string fileName = Path.GetFileName(filePath);
            var startTime = DateTime.Now;
            FileStream fs = null;
            IPEndPoint clientEndPoint = clientSocket.RemoteEndPoint as IPEndPoint;
            int index = 0;
            Console.WriteLine($"Starting to receive {fileName} from the server");
            FileMode filemode = FileMode.Create;
            if (fileOffset > 0)
            {
                filemode = FileMode.OpenOrCreate;
                Console.WriteLine($"File already exist, continue downloading from client");
            }

            try
            {
                fs = new FileStream($"{fileName}", filemode, FileAccess.Write);
                if (fileOffset != 0)
                {
                    fs.Seek((long)fileOffset, SeekOrigin.Begin);
                }

                byte[] buffer = new byte[Program.bufferSize];
                int bytesRead;
                while ((bytesRead = clientSocket.Receive(buffer)) > 0)
                {
                    index += Program.bufferSize;
                    string response = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    if (response.Contains("end_file_transfer"))
                    {
                        var endTime = DateTime.Now;
                        var deltaTime = endTime - startTime;
                        var bitrate = index / deltaTime.Seconds;
                        Console.WriteLine($"File transfer completed. Bitrate: {bitrate / 1024} kb/s.");
                        break;
                    }


                    fs.Write(buffer, 0, bytesRead);
                }

            }
            catch (SocketException e)
            {
                var client = new ClientData();
                client.clientSocket = clientSocket;
                client.mode = "upload";
                client.port = clientEndPoint.Port;
                client.ipAddres = clientEndPoint.Address.ToString();
                client.index = index;
                client.filename = filePath;
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
            }
            finally
            {
                if (fs != null)
                {
                    fs.Close();
                }
            }
        }
        

        private void DownloadFromServer(Socket clientSocket, string fileName, long filePosition = 0)
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
            try
            {
                Console.WriteLine($"Sending {fileName} file to the client.");
                Console.WriteLine($"start_file_transfer {fileName} {filePosition}");
                byte[] startMsg = Encoding.ASCII.GetBytes($"start_file_transfer {fileName} {filePosition}");
                clientSocket.Send(startMsg);

                using (FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read))
                {
                    fs.Seek(filePosition, SeekOrigin.Begin);
                    byte[] buffer = new byte[Program.bufferSize];
                    int bytesRead;


                    while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        index += Program.bufferSize;
                        clientSocket.Send(buffer, 0, bytesRead, SocketFlags.None);
                    }
                }

                Thread.Sleep(1000);

                byte[] endMsg = Encoding.ASCII.GetBytes("end_file_transfer");
                clientSocket.Send(endMsg);

                Console.WriteLine($"File {fileName} has been sent to the client.");

            }
            catch (Exception e)
            {
                var client = new ClientData();
                client.clientSocket = clientSocket;
                client.mode = "download";
                client.port = clientEndPoint.Port;
                client.ipAddres = clientEndPoint.Address.ToString();
                client.index = index;
                client.filename = fileName;
                clientsProgress.TryAdd($"{clientEndPoint.Address.ToString()}:{clientEndPoint.Port}", client);
                unconnectedClientDatas.Add(client);
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
    }
}
