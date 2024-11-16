using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Text.Json;
using System.Reflection.Metadata;

class ClientHandler
{
    public TcpClient Client { get; set; }
    public int ClientId { get; set; }
    public string ClientName { get; set; }
    public NetworkStream Stream { get; set; }
    public DateTime LastActivity { get; set; }
    public int? CurrentConversationWith { get; set; } // ID of the client the user is currently talking to

    public ClientHandler(TcpClient client, int clientId)
    {
        Client = client;
        ClientId = clientId;
        Stream = client.GetStream();
        LastActivity = DateTime.Now;
        CurrentConversationWith = null;
    }
}

class Program
{
    private static List<ClientHandler> clients = new List<ClientHandler>();
    private static int nextClientId = 1;
    private static readonly object lockObj = new object();

    static void Main(string[] args)
    {
        TcpListener server = new TcpListener(IPAddress.Any, 8888);
        server.Start();
        Console.WriteLine("Server started...");

        Thread monitorThread = new Thread(MonitorClients);
        monitorThread.Start();

        while (true)
        {
            TcpClient client = server.AcceptTcpClient();
            lock (lockObj)
            {
                var clientHandler = new ClientHandler(client, nextClientId++);
                clients.Add(clientHandler);
                Console.WriteLine($"Client {clientHandler.ClientId} connected.");

                // Send assigned Client ID to the client
                SendClientId(clientHandler);

                Thread clientThread = new Thread(HandleClient);
                clientThread.Start(clientHandler);
            }
        }
    }

    private static void SendClientId(ClientHandler clientHandler)
    {
        var message = new Message
        {
            SenderId = 0, // 0 means server
            ReceiverId = clientHandler.ClientId,
            Content = $"Your assigned client ID is {clientHandler.ClientId}.",
            Timestamp = DateTime.UtcNow.ToString("o"),
            ConversationId = -1
        };
        string jsonResponse = JsonSerializer.Serialize(message);
        byte[] buffer = Encoding.UTF8.GetBytes(jsonResponse);
        clientHandler.Stream.Write(buffer, 0, buffer.Length);
    }

    private static void HandleClient(object obj)
    {
        ClientHandler clientHandler = (ClientHandler)obj;
        byte[] buffer = new byte[1024];
        int bytesRead;

        try
        {
            while (true)
            {
                bytesRead = clientHandler.Stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) break; // Client disconnected

                string jsonMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                Message message = JsonSerializer.Deserialize<Message>(jsonMessage);
                Console.WriteLine($"Received from Client {message.SenderId}: {message.Content}");
                clientHandler.LastActivity = DateTime.Now;


                if (message.Content == "heartbeat")
                {
                    // Update the last activity timestamp without logging or processing
                    clientHandler.LastActivity = DateTime.Now;
                    continue;
                }

                // Handle commands
                if (message.Content == "/list")
                {
                    SendClientList(clientHandler);
                }
                else if (message.Content.StartsWith("/connect"))
                {
                    HandleConnectCommand(message.Content, clientHandler);
                }
                else if (message.Content == "/exit")
                {
                    clientHandler.CurrentConversationWith = null;
                    SendMessageToClient(clientHandler, "Exited conversation. You can start a new one with /connect <client_id>.");
                }
                else if (message.Content.StartsWith("/acknoledgment"))
                {
                    HandleAcknoledgment(message);
                }
                else if (clientHandler.CurrentConversationWith.HasValue)
                {
                    SendPrivateMessage(clientHandler, message);
                }
                else
                {
                    SendMessageToClient(clientHandler, "You must start a conversation with /connect <client_id> before sending messages.");
                }
            }
        }
        catch (Exception)
        {
            // Handle client disconnection
        }
        finally
        {
            lock (lockObj)
            {
                clients.Remove(clientHandler);
            }
            clientHandler.Client.Close();
            Console.WriteLine($"Client {clientHandler.ClientId} disconnected.");
        }

    }

    private static void SendClientList(ClientHandler requester)
    {
        StringBuilder clientList = new StringBuilder("Active clients:\n");
        lock (lockObj)
        {
            foreach (var client in clients)
            {
                if (client.ClientId != requester.ClientId)
                {
                    clientList.AppendLine($"ID: {client.ClientId}, Name: {client.ClientName ?? "Anonymous"}");
                }
            }
        }
        SendMessageToClient(requester, clientList.ToString());
    }

    private static void HandleConnectCommand(string message, ClientHandler sender)
    {
        string[] parts = message.Split(' ', 2);
        if (parts.Length != 2 || !int.TryParse(parts[1], out int recipientId))
        {
            SendMessageToClient(sender, "Usage: /connect <client_id>");
            return;
        }

        lock (lockObj)
        {
            var recipient = clients.Find(c => c.ClientId == recipientId);
            if (recipient != null)
            {
                sender.CurrentConversationWith = recipientId;
                SendMessageToClient(sender, $"You are now connected to Client {recipientId}. Type your messages.");
            }
            else
            {
                SendMessageToClient(sender, "Client not found.");
            }
        }
    }


    private static void SendPrivateMessage(ClientHandler sender, Message message)
    {
        lock (lockObj)
        {
            var recipient = clients.Find(c => c.ClientId == sender.CurrentConversationWith);
            if (recipient != null)
            {
                Message newMessage = new Message
                {
                    SenderId = sender.ClientId,
                    ReceiverId = recipient.ClientId,
                    Content = message.Content,
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    ConversationId = (int)sender.CurrentConversationWith
                };
                string jsonResponse = JsonSerializer.Serialize(newMessage);
                byte[] buffer = Encoding.UTF8.GetBytes(jsonResponse);
                recipient.Stream.Write(buffer, 0, buffer.Length);

                // Send acknowledgment to sender
                Message ackMessage = new Message
                {
                    SenderId = 0,
                    ReceiverId = recipient.ClientId,
                    Content = "Message delivered.",
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    ConversationId = (int)sender.CurrentConversationWith
                };
                string jsonAck = JsonSerializer.Serialize(ackMessage);
                buffer = Encoding.UTF8.GetBytes(jsonAck);
                sender.Stream.Write(buffer, 0, buffer.Length);
            }
            else
            {
                SendMessageToClient(sender, "The client you were connected to is no longer available.");
                sender.CurrentConversationWith = null; // Reset the conversation state
            }
        }
    }

    private static void SendMessageToClient(ClientHandler clientHandler, string content)
    {
        Message message = new Message
        {
            SenderId = 0, // 0 means server
            ReceiverId = clientHandler.ClientId,
            Content = content,
            Timestamp = DateTime.UtcNow.ToString("o"),
            ConversationId = 0 // No specific conversation ID for server messages
        };
        string jsonResponse = JsonSerializer.Serialize(message);
        byte[] buffer = Encoding.UTF8.GetBytes(jsonResponse);
        clientHandler.Stream.Write(buffer, 0, buffer.Length);
    }

    private static void HandleAcknoledgment(Message sender_message)
    {
        lock (lockObj)
        {
            var sender = clients.Find(c => c.ClientId == sender_message.SenderId);
            var recipient = clients.Find(c => c.ClientId == sender_message.ReceiverId);
            if (recipient != null)
            {
                // Send acknowledgment to sender
                Message ackMessage = new Message
                {
                    SenderId = 0,
                    ReceiverId = recipient.ClientId,
                    Content = "Message Reached!.",
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    ConversationId = sender_message.ReceiverId
                };
                
                string jsonAck = JsonSerializer.Serialize(ackMessage);
                byte[] buffer = Encoding.UTF8.GetBytes(jsonAck);
                recipient.Stream.Write(buffer, 0, buffer.Length);
            }
            else
            {
                SendMessageToClient(sender, "The client you were connected to is no longer available.");
                sender.CurrentConversationWith = null; // Reset the conversation state
            }
        }
    }

    private static void MonitorClients()
    {
        while (true)
        {
            Thread.Sleep(1000); // Check every second
            lock (lockObj)
            {
                for (int i = clients.Count - 1; i >= 0; i--)
                {
                    if ((DateTime.Now - clients[i].LastActivity).TotalSeconds > 10)
                    {
                        Console.WriteLine($"Client {clients[i].ClientId} is offline (inactive for over 10 seconds). Disconnecting...");
                        clients[i].Client.Close();
                        clients.RemoveAt(i);
                    }
                }
            }
        }
    }
}

public class Message
{
    public int SenderId { get; set; }
    public int ReceiverId { get; set; }
    public string Content { get; set; }
    public string Timestamp { get; set; }
    public int ConversationId { get; set; }
}