using System.Net.WebSockets;
using System.Text;

namespace ToDoListAPI.Services
{
    public class WebSocketService
    {
        private readonly List<WebSocket> _clients = new();

        public void AddClient(WebSocket client)
        {
            lock (_clients)
            {
                _clients.Add(client);
            }
        }

        public async Task Broadcast(string message)
        {
            lock (_clients)
            {
                var buffer = Encoding.UTF8.GetBytes(message);
                var tasks = new List<Task>();
                foreach (var client in _clients.ToArray())
                {
                    if (client.State == WebSocketState.Open)
                    {
                        tasks.Add(client.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None));
                    }
                }
                Task.WhenAll(tasks).Wait(); // Ждём завершения всех отправок
            }
        }

        public void RemoveClient(WebSocket client)
        {
            lock (_clients)
            {
                _clients.Remove(client);
            }
        }

        public async Task HandleWebSocket(WebSocket webSocket)
        {
            AddClient(webSocket);
            var buffer = new byte[1024 * 4];
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    RemoveClient(webSocket);
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", CancellationToken.None);
                    break;
                }
            }
        }
    }
}