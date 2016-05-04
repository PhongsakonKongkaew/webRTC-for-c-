using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.WebSockets;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace client
{
    class Program
    {
        // class SenseWebSocketClient
        private static SenseWebSocketClient client = null;

        // path websocket 
        static String url = "ws://192.168.182.135:8888/kurento";

        // Message ตัวอย่างที่ส่งเข้าไป
        static string[] cmd_meassage = { "{\"id\":1,\"method\":\"ping\",\"params\":{\"interval\":240000},\"jsonrpc\":\"2.0\"}",
                                         "{\"id\":2,\"method\":\"create\",\"params\":{\"type\":\"MediaPipeline\",\"constructorParams\":{}},\"jsonrpc\":\"2.0\"}" };
        
        // สถานะของการ Connect ว่่าสามารถเชื่อมต่อกับ kurento media server โดยผ่าน websocket ได้หรือไม่
        static Boolean connect_st = false;

        // เริ่มการทำงาน websocket
        static async Task Start_webSocket()
        {
            client = new SenseWebSocketClient(new Uri(url));
            Console.WriteLine("Connecting to "+url);
            connect_st = await client.Connect_webScoket();

        }

        // รับ message ที่ได้รับหลังจากที่ send message ไป
        static async Task<string> GetRecieve()
        {
            var docList = await client.Receive();
            Console.WriteLine("Receive : " + docList + "\n");
            return docList;  
        }

        // Main การทำงาน
        static void Main(string[] args)
        {
            Console.WriteLine("============= Start =============");
            Start_webSocket();      // เรื่มเชื่อมต่อ kms ผ่าน websocket

            Thread.Sleep(1000);     // wait for a sec, so server starts and ready to accept connection..

            int i = 0;

            // เมื่อสามารถ connect kms ได้ผ่าน websocket
            if(connect_st){
                
                // loop ในการส่ง message และรับ message 
                while (i<cmd_meassage.Length)
                {

                    Console.WriteLine("*********** Message " + (i+1) + " *************\n");
                    Console.WriteLine("===== Send message =====");
                    
                    // ส่ง message 
                    client.SendCommand(cmd_meassage[i]);
                    Console.WriteLine("Send    : " + cmd_meassage[i]+ "\n");

                    Console.WriteLine("===== Receive message =====");
                    // รับ message
                    GetRecieve();

                    Thread.Sleep(1000);

                    Console.WriteLine("Press any key to continue...");
                    Console.ReadLine();
                    Console.Clear();
                    i++;
                }
            }

            Thread.Sleep(1000);
            Console.WriteLine("Press any key to exit...");
            Console.ReadLine();
        }
    }
}
