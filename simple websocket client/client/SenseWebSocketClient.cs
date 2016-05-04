using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;  

namespace client
{
    public class SenseWebSocketClient
    {
        private ClientWebSocket _client;
        public Uri _senseServerURI;

        public SenseWebSocketClient(Uri senseServerURI)
        {
            _client = new ClientWebSocket();
            _senseServerURI = senseServerURI;
        }

        // Connect websocket
        public async Task<Boolean> Connect_webScoket()
        {
            Boolean result = false;

            Thread.Sleep(1000); //wait for a sec, so server starts and ready to accept connection..

            try
            {
                await _client.ConnectAsync(_senseServerURI, CancellationToken.None);
                Console.WriteLine("connected...\n");
                result = true;
            }
            catch (Exception ex)
            {
                //Console.WriteLine("Exception: {0}", ex);
                Console.WriteLine("Cannot connect webSocket...\n");
                result = false;
            }

            return result;
        }

        private async Task ConnectToSenseServer()
        {
            await _client.ConnectAsync(_senseServerURI, CancellationToken.None);
        }

        // Send message ในรูปแบบของ JSON
        public async Task SendCommand(string jsonCmd)
        {
            ArraySegment<byte> outputBuffer = new ArraySegment<byte>(Encoding.UTF8.GetBytes(jsonCmd));
            await _client.SendAsync(outputBuffer, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        // รับ message 
        public async Task<string> Receive()
        {
            var receiveBufferSize = 1024;
            byte[] buffer = new byte[receiveBufferSize];
            var result = await _client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            var resultJson = (new UTF8Encoding()).GetString(buffer);
            return resultJson;
        }
    }  
}
