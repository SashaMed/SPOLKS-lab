using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SPOLKS_lab
{
    internal class Client
    {
        private Socket clientSocket;
        private string serverIp;
        private string clientIp;
        private int serverPort;
        private int clientPort;
        private bool connectionClose;
        private bool canSendToServer;

        public Client(string serverIp, int serverPort, string clientIp, int clientPort)
        {
            canSendToServer = true;
            this.serverIp = serverIp;
            this.serverPort = serverPort;
            this.clientIp = clientIp;
            this.clientPort = clientPort;
            IPAddress ipAddress = IPAddress.Parse(clientIp);
            IPEndPoint localEndPoint = new IPEndPoint(ipAddress, clientPort);


            clientSocket = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            try
            {

                clientSocket.Bind(localEndPoint);
                //Console.WriteLine($"Socket connected to {clientSocket.RemoteEndPoint.ToString()}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Unable to connect to remote endpoint: {e.ToString()}");
            }
        }


        public Client(string serverIp, int serverPort)
        {
            canSendToServer = true;
            this.serverIp = serverIp;
            this.serverPort = serverPort;
            clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }

        public void Start()
        {
            try
            {
                IPEndPoint serverEndPoint = new IPEndPoint(IPAddress.Parse(serverIp), serverPort);
                clientSocket.Connect(serverEndPoint);


                //Thread receiveThread = new Thread(() => ReceiveData());
                //receiveThread.Start();

                while (true)
                {
                    if (connectionClose)
                    {
                        break;
                    }
                    if (InputUpdate())
                    {
                        var response = CheckResponse();
                        Console.Write("\nServer response: " + response + "\n>>");
                    }
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

        private bool InputUpdate()
        {
            if (connectionClose)
            {
                return false;
            }
            Console.Write(">>");
            string message = Console.ReadLine();
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }
            var splitedMessage = message.Split(' ');
            if (splitedMessage.Length > 1)
            {
                if (splitedMessage[0] == "upload")
                {
                    UploadToServer(splitedMessage[1]);
                    return false;
                }
                if (splitedMessage[0] == "download")
                {
                    DownloadFromServer(message);
                    return false;
                }
            }
            byte[] data = Encoding.ASCII.GetBytes(message);
            if (connectionClose)
            {
                return false;
            }
            if (!canSendToServer)
            {
                Console.WriteLine("cant send to server now");
                return false;
            }
            if (message == "")
            {
                return false;
            }
            clientSocket.Send(data);
            return true;
        }

        private void Send(string message)
        {
            if (connectionClose) 
            {
                return;
            }
            try
            {
                byte[] data = Encoding.ASCII.GetBytes(message);
                clientSocket.Send(data);
            }
            catch (Exception e)
            {
                connectionClose = true;
                clientSocket.Close();
                Console.WriteLine("--------------------------------------------");
                Console.WriteLine("SocketException in Send: " + e);
                Console.WriteLine("--------------------------------------------");
            }
        }

        private string CheckResponse()
        {
            try
            {
                byte[] buffer = new byte[Program.bufferSize];
                int bytesRead;
                bytesRead = clientSocket.Receive(buffer);
                var response = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                var splitedResponse = response.Split(' ');
                CheckForClosedConnection(response);
                //Console.Write("\nServer response: " + response + "\n>>");
                return response;
            }
            catch(Exception e)
            {
                connectionClose = true;
                clientSocket.Close();
                Console.WriteLine("SocketException in CheckResponse: " + e);
                Console.WriteLine("--------------------------------------------");
                return "";
            }
        }

        private bool CheckForClosedConnection(string response)
        {
            if (response == "closing connection")
            {
                Console.Write("\nServer closed connection.\n");
                connectionClose = true;
                return true;
            }
            return false;
        }


        private void ReceiveData()
        {
            try
            {
                byte[] buffer = new byte[Program.bufferSize];
                int bytesRead;
                while ((bytesRead = clientSocket.Receive(buffer)) > 0)
                {
                    var response = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    var splitedResponse = response.Split(' ');
                    if (CheckForClosedConnection(response))
                    {
                        break;
                    }
                    if (splitedResponse.Length > 1)
                    {
                        if (splitedResponse[0] == "start_file_transfer")
                        {
                            Console.Write("\nstart_file_transfer\n");
                            //Thread.Sleep(1000);
                            //DownloadFromServer(splitedResponse[1], ulong.Parse(splitedResponse[2]));
                        }

                        if (splitedResponse[0] == "request_for_upload")
                        {
                            var index = ulong.Parse(splitedResponse[2]);
                            UploadToServer(splitedResponse[1], index);
                        }
                    }
                    Console.Write("\nServer response: " + response + "\n>>");
                }
            }
            catch (SocketException e)
            {
                Console.WriteLine("\n--------------------------------------------");
                connectionClose = true;
                clientSocket.Close();
                Console.WriteLine("SocketException in ReceiveData: " + e);
                Console.WriteLine("--------------------------------------------");
            }


            catch (Exception e)
            {
                Console.WriteLine("\n--------------------------------------------");
                connectionClose = true;
                clientSocket.Close();
                Console.WriteLine("Exception in ReceiveData: " + e);
                Console.WriteLine("--------------------------------------------");
            }
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
                var response = CheckResponse();
                var splitedResponse = response.Split(' ');
                Console.WriteLine("response length: " + splitedResponse.Length);
                foreach (var item in splitedResponse)
                {
                    Console.WriteLine(item);
                }
                fileOffset = ulong.Parse(splitedResponse[2]);


                canSendToServer = false;
                Console.WriteLine($"Sending {fileName} file to the server.");
                //byte[] startMsg = Encoding.ASCII.GetBytes("start_file_transfer " + fileName + " " + fileOffset);
                //clientSocket.Send(startMsg);

                using (FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read))
                {

                    if (fileOffset != 0)
                    {
                        fs.Seek((long)fileOffset, SeekOrigin.Begin);
                    }
                    byte[] buffer = new byte[Program.bufferSize];
                    int bytesRead;


                    while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        clientSocket.Send(buffer, 0, bytesRead, SocketFlags.None);
                    }
                }

                Thread.Sleep(1000);

                byte[] endMsg = Encoding.ASCII.GetBytes("end_file_transfer");
                clientSocket.Send(endMsg);
                canSendToServer = true;
                Console.WriteLine($"File {fileName} has been sent to the server.");

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
            canSendToServer = false;

            Send(download);
            //canSendToServer = false;
            var startMessage = CheckResponse();

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
                        var bitrate = index / deltaTime.Milliseconds;
                        Console.Write($"\nFile transfer completed. Bitrate: {bitrate/1024} kb/ms.\n>>");
                        break;
                    }


                    fs.Write(buffer, 0, bytesRead);
                    canSendToServer = true;
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
