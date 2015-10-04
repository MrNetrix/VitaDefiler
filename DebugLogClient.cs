using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VitaDefiler
{
    internal class DebugLogClient
    {
        const int PORT_NUMBER = 18194;
        const string LINE_PREFIX = "[Vita]  ";
        const string NEW_LINE = "\n";
        const string NEW_LINE_REPLACE = NEW_LINE + LINE_PREFIX;

        private static UdpClient udp;
        private static IPEndPoint ip = new IPEndPoint(IPAddress.Any, PORT_NUMBER);
        private static bool firstMessage = true;

        public static void Start()
        {
            if (udp == null)
            {
                Defiler.MsgLine("Starting to listen for DebugNet output");

                try
                {
                    udp = new UdpClient(PORT_NUMBER);
                }
                catch (Exception e)
                {
                    Defiler.ErrLine("Failed to start listening for DebugNet output. Is there another instance of VitaDefiler running?");
                    return;
                }

                StartListening();
            }
        }

        public static void Stop()
        {
            try
            {
                if (udp != null)
                {
                    udp.Close();
                    udp = null;
                    Defiler.MsgLine("Stopped listening for DebugNet output");
                }
            }
            catch { /* don't care */ }
        }

        private static void StartListening()
        {
            udp.BeginReceive(Receive, new object());
        }

        private static void Receive(IAsyncResult ar)
        {
            try
            {
                if (udp != null)
                {
                    byte[] bytes = udp.EndReceive(ar, ref ip);
                    string message = Encoding.ASCII.GetString(bytes).Replace(NEW_LINE, NEW_LINE_REPLACE);

                    if (firstMessage)
                    {
                        Defiler.Log(LINE_PREFIX + "{0}", message);
                        firstMessage = false;
                    }
                    else
                    {
                        Defiler.Log("{0}", message);
                    }

                    StartListening();
                }
            }
            catch
            {
                if (udp != null)
                {
                    Stop();
                }
            }
        }
    }
}
