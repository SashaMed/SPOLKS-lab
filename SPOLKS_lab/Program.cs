namespace SPOLKS_lab
{
    internal class Program
    {


        public static int bufferSize = 1024;    
        static void Main(string[] args)
        {
            HandleInput();
        }

        public static int CustomStringHashCode(string input)
        {
            const int seed = 23; // Просто какое-то число для начального значения
            int hash = seed;

            foreach (char c in input)
            {
                hash = hash * 31 + c; // Простая хеш-функция
            }

            return hash;
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
                            ServerUDP server = new ServerUDP(splitedInput[1], int.Parse(splitedInput[2]));
                            server.Start();
                        }
                        else if (splitedInput[0] == "client")
                        {
                            ClientUDP client = new ClientUDP(splitedInput[1], int.Parse(splitedInput[2]), splitedInput[3], int.Parse(splitedInput[4]) );
                            client.Start();
                        }
                    }
                    else
                    {
                        if (input == "server")
                        {
                            ServerUDP server = new ServerUDP("127.0.0.1", 12346);
                            server.Start();
                        }
                        else if (input == "client")
                        {
                            ClientUDP client = new ClientUDP("127.0.0.1", 12346);
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