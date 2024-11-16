using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace ConsoleAppTeste
{
    public class ChatServer
    {
        private static List<TcpClient> clients = new List<TcpClient>();
        private static readonly object lockObj = new object();

        public void StartServer(int port)
        {
            TcpListener listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine("Server is listening on port " + port);

            while (true)
            {
                TcpClient client = listener.AcceptTcpClient();

                Console.WriteLine("Client connected.");

                // Start a new thread to handle the client
                Thread clientThread = new Thread(HandleClient);
                clientThread.Start(client);
            }
        }

        private void HandleClient(object clientObj)
        {
            TcpClient client = (TcpClient)clientObj;
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];
            int bytesRead;

            while (true)
            {
                try
                {
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break; // Client disconnected
                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Console.WriteLine("Received: " + message);
                    BroadcastMessage(message, client);
                }
                catch (Exception)
                {
                    Console.WriteLine("An error occurred break the loop");
                    break; // An error occurred, break the loop
                }
            }

            lock (lockObj)
            {
                clients.Remove(client);
            }
            client.Close();

            client.Close();
        }
        private static void BroadcastMessage(string message, TcpClient sender)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            lock (lockObj)
            {
                foreach (var client in clients)
                {
                    if (client != sender)
                    {
                        client.GetStream().Write(buffer, 0, buffer.Length);
                    }
                }
            }
        }
    }
}
