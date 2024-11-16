import socket
import threading
import time
import json

receiver_id = 0
conversation_id = 0

def receive_messages(sock, client_id):
    
    while True:
        try:
            message = sock.recv(1024).decode('utf-8')
            if message:
                # Deserialize the JSON message
                data = json.loads(message)
                print(f"\n{data['Content']}")
                if 'SenderId' in data and data['SenderId'] == 0:
                    print("Server: " + data['Content'])
                elif 'SenderId' in data and data['SenderId'] != 0:
                    acknoledgment(sock, data, client_id)
                    

                    
        except Exception as e:
            print("An error occurred:", e)
            break

def send_messages(sock, client_id):
    global conversation_id
    global receiver_id
    while True:
        message = input()
        if message.lower() == 'exit':
            break
        if message.lower() == '/exit':
            conversation_id = 0
            receiver_id = 0
        if message:
            # Create a message dictionary
            msg_dict = {
            "SenderId": client_id,  # Use the assigned client ID
            "ReceiverId": receiver_id,  # This can be set when connecting to another client
            "Content": message,
            "Timestamp": time.strftime("%Y-%m-%dT%H:%M:%S", time.gmtime()),
            "ConversationId": conversation_id
            }
        # Serialize the dictionary to JSON
        json_message = json.dumps(msg_dict)
        sock.send(json_message.encode('utf-8'))

def send_heartbeat(sock, client_id):
    
    while True:
        time.sleep(3)  # Send a heartbeat every 5 seconds
        try:
            # Create a message dictionary
            msg_dict = {
                "SenderId": client_id,  # Use the assigned client ID
                "ReceiverId": receiver_id,  # This can be set when connecting to another client
                "Content": "heartbeat",
                "Timestamp": time.strftime("%Y-%m-%dT%H:%M:%S", time.gmtime()),
                "ConversationId": conversation_id
            }
            # Serialize the dictionary to JSON
            json_message = json.dumps(msg_dict)
            sock.send(json_message.encode('utf-8'))
        except:
            break  # Stop if the connection is closed

def acknoledgment(sock, data, client_id):
    global conversation_id
    global receiver_id
    
    if data['SenderId'] != conversation_id:
                conversation_id = data['SenderId']
                receiver_id = data["SenderId"]
                msg_dict = {
                "SenderId": client_id,  # Use the assigned client ID
                "ReceiverId": receiver_id,  # This can be set when connecting to another client
                "Content": "/acknoledgment",
                "Timestamp": time.strftime("%Y-%m-%dT%H:%M:%S", time.gmtime()),
                "ConversationId": conversation_id
                }
               # Serialize the dictionary to JSON
                json_message = json.dumps(msg_dict)
                sock.send(json_message.encode('utf-8'))
    else:
                msg_dict = {
                "SenderId": client_id,  # Use the assigned client ID
                "ReceiverId": receiver_id,  # This can be set when connecting to another client
                "Content": "/acknoledgment",
                "Timestamp": time.strftime("%Y-%m-%dT%H:%M:%S", time.gmtime()),
                "ConversationId": conversation_id
                }
                # Serialize the dictionary to JSON
                json_message = json.dumps(msg_dict)
                sock.send(json_message.encode('utf-8'))


def main():
    server_ip = "127.0.0.1"  # Change this to the server's IP if needed
    server_port = 8888

    client_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    client_socket.connect((server_ip, server_port))

    # Start the thread to receive messages
    

    # Keep the main thread open to send messages
    client_id = None
    while client_id is None:
        # Receive the assigned client ID
        message = client_socket.recv(1024).decode('utf-8')
        if message:
            data = json.loads(message)
            if 'Content' in data and 'Your assigned client ID is' in data['Content']:
                client_id = data['ReceiverId']
                print(f"Assigned Client ID: {client_id}")
    
    threading.Thread(target=receive_messages, args=(client_socket,client_id,), daemon=True).start()
    threading.Thread(target=send_heartbeat, args=(client_socket, client_id,), daemon=True).start()
    print("Enter command ('/list' to see clients, '/connect <client_id>' to start conversation, '/exit' to leave conversation, 'exit' to quit): ")
    send_messages(client_socket, client_id)
    
    client_socket.close()
            



if __name__ == "__main__":
    main()
