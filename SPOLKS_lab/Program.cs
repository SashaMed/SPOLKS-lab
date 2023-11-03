namespace SPOLKS_lab
{
    internal class Program
    {


        public static int bufferSize = 1024;    
        static void Main(string[] args)
        {
            HandleInput();
        }

        static void HandleInput()
        {
            while (true)
            {
                try
                {
                    Console.Write("Mode\n>>");
                    var input = Console.ReadLine();
                    var splitedInput = input.Split(' ');

                    if (splitedInput.Length > 1)
                    {
                        if (splitedInput[0] == "server")
                        {
                            Server server = new Server(splitedInput[1], int.Parse(splitedInput[2]));
                            server.Start();
                        }
                        else if (splitedInput[0] == "client")
                        {
                            Client client = new Client(splitedInput[1], int.Parse(splitedInput[2]), splitedInput[3], int.Parse(splitedInput[4]) );
                            client.Start();
                        }
                    }
                    else
                    {
                        if (input == "server")
                        {
                            Server server = new Server("127.0.0.1", 12346);
                            server.Start();
                        }
                        else if (input == "client")
                        {
                            Client client = new Client("127.0.0.1", 12346);
                            client.Start();
                        }
                        else if (input == "exit")
                        {
                            return;
                        }
                    }
                }
                catch(Exception e)
                {
                    Console.WriteLine("\n--------------------------------------------");
                    Console.WriteLine("Exception at Program: " + e.ToString());
                    Console.WriteLine("--------------------------------------------");
                    continue;
                }
            }
        }
    }
}