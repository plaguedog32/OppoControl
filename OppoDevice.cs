using System.Diagnostics;
using System.Net.Sockets;
using System.Net;
using System.Text.RegularExpressions;
using System.Text;

namespace OppoControl
{
    public class OppoDirectoryCompare : IComparer<DirectoryInfo>
    {
        private static Regex NumStartRegex = new(@"^(?<Num>\d+)", RegexOptions.Compiled);
        private IComparer<DirectoryInfo> _comparerImplementation;
        public int Compare(DirectoryInfo? x, DirectoryInfo? y)
        {
            string xName = (x?.Name ?? string.Empty).ToLower();
            string yName = (y?.Name ?? string.Empty).ToLower();

            var xMatch = NumStartRegex.Match(xName);
            var yMatch = NumStartRegex.Match(yName);

            if (xMatch.Success && yMatch.Success)
            {
                var xNum = int.Parse(xMatch.Groups["Num"].Value);
                var yNum = int.Parse(yMatch.Groups["Num"].Value);

                if (xNum < yNum)
                {
                    return -1;
                }

                if (yNum < xNum)
                {
                    return 1;
                }
            }
            else if (xMatch.Success && !yMatch.Success)
            {
                return -1;
            }
            else if (!xMatch.Success && yMatch.Success)
            {
                return 1;
            }

            for (int i = 0; i < xName.Length; i++)
            {
                if (i >= yName.Length)
                {
                    return -1;
                }

                if (xName[i] == '(' && yName[i] >= '0')
                {
                    return 1;
                }
                else if (yName[i] == '(' && xName[i] >= '0')
                {
                    return -1;
                }

                var c = xName[i].CompareTo(yName[i]);

                if (c == 0)
                {
                    continue;
                }

                return c;
            }

            return string.Compare(xName, yName, StringComparison.Ordinal);
        }
    }

    public enum RemoteKey
    {
        UpArrow,
        DownArrow,
        LeftArrow,
        RightArrow,
        Enter,
        PageUp,
        PageDown,
        Return,
        Home,
        Stop,
        Power,
        Open,
        On,
        Off,
        Dimmer,
        PureAudio,
        VolInc,
        VolDec,
        Mute,
        Num1,
        Num2,
        Num3,
        Num4,
        Num5,
        Num6,
        Num7,
        Num8,
        Num9,
        Num0,
        Clear,
        Goto,
        Info,
        TopMenu,
        PopUpMenu,
        Setup,
        Red,
        Green,
        Blue,
        Yellow,
        Play,
        Pause,
        SkipPrev,
        Rev,
        Fwd,
        SkipNext,
        Audio,
        Subtitle,
        Angle,
        Zoom,
        SAP,
        ABReplay,
        Repeat,
        PIP,
        Resolution,
        SubtitleHold,
        Option,
        _3D,
        Pic,
        HDR,
        InfoHold,
        ResolutionHold,
        ShowAVSync,
        GaplessPlayback,
        Input
    }

    public class OppoStatusEvent
    {
        public string Message { get; set; }
    }

    public class OppoUnit
    {
        private static Regex BroadCastMessageRegex = new(@"Notify:(?<MessageName>.*)\sServer IP:(?<IP>[\d\.]+)\sServer Port:(?<Port>\d+)\sServer Name:(?<Name>[\w \-]+)", RegexOptions.Compiled);
        private static Regex QueryDirectoryResponse = new(@"OK (?<Type>D|U|O|0|F|L|S|N) (?<Value>.*)", RegexOptions.Compiled);
        private static Regex NumberResponse = new(@"OK (?<Num>\d+)", RegexOptions.Compiled);
        private static Regex OKResponse = new(@"OK", RegexOptions.Compiled);

        public EventHandler<OppoStatusEvent> StatusEventHandler { get; set; }

        public EventHandler Connected { get; set; }
        public EventHandler Disconnected { get; set; }

        public OppoUnit()
        {
            TelnetRecvThread = new Thread(TelnetRecvWorkFunc);
            TelnetRecvThread.Start();
            ProcessStatusThread = new Thread(StatusUpdateWorkFunc);
            ProcessStatusThread.Start();
            KeepRemotePortOpenThread = new Thread(KeepRemotePortOpenWorkFunc);
            KeepRemotePortOpenThread.Start();
        }

        public void Connect()
        {
            WaitForOppo();
        }

        private IPAddress GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip;
                }
            }
            throw new Exception("No network adapters with an IPv4 address in the system!");
        }

        private void KeepRemotePortOpenWorkFunc()
        {
            int PORT = 7624;
            UdpClient udpClient = new UdpClient();
            var localIP = GetLocalIPAddress();
            udpClient.Client.Bind(new IPEndPoint(localIP, PORT));
            var msg = "NOTIFY OREMOTE LOGIN";
            var lastSend = DateTime.MinValue;
            var msgRecieved = false;
            var from = new IPEndPoint(0, 0);
            while (!_shutdown)
            {
                if (udpClient.Available > 0)
                {
                    var recvBuffer = udpClient.Receive(ref from);
                    if (from.Address.ToString() == localIP.ToString())
                    {
                        continue;
                    }

                    var recvMsg = Encoding.ASCII.GetString(recvBuffer);

                    if (recvMsg.StartsWith("Notify:OPPO Player Start"))
                    {
                        continue;
                    }

                    msgRecieved = true;
                    Console.WriteLine("Remote port keep alive response");
                }

                if (DateTime.Now < lastSend + TimeSpan.FromSeconds(msgRecieved ? 60*10 : 1))
                {
                    Thread.Sleep(1);
                    continue;
                }
                var data = Encoding.ASCII.GetBytes(msg);
                udpClient.Send(data, data.Length, "255.255.255.255", PORT);
                lastSend = DateTime.Now;
            }
        }

        private void WaitForOppo()
        {
            Socket sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            EndPoint ep = new IPEndPoint(IPAddress.Any, 7624);
            sock.Bind(ep);
            Console.WriteLine("Waiting For Oppo to send broadcast message...");

            while (true)
            {
                byte[] data = new byte[1024];
                int recv = sock.ReceiveFrom(data, ref ep);
                string stringData = Encoding.ASCII.GetString(data, 0, recv);

                var match = BroadCastMessageRegex.Match(stringData);
                if (match.Success)
                {
                    try
                    {
                        Port = ushort.Parse(match.Groups["Port"].Value);
                        Address = IPAddress.Parse(match.Groups["IP"].Value);
                        Name = match.Groups["Name"].Value;
                        Console.WriteLine($"Found Oppo ({Name}) at {Address}:{Port}");
                        break;
                    }
                    catch (Exception e)
                    {
                    }
                }
            }
            sock.Close();
            Connected?.Invoke(null, EventArgs.Empty);

            Client = new TcpClient(Address.ToString(), Port);
        }

        private void StatusUpdateWorkFunc()
        {
            while (!_shutdown)
            {
                if (_statusMessages.Count > 0)
                {
                    StatusEventHandler?.Invoke(null, new OppoStatusEvent{Message = _statusMessages.Dequeue()});
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
        }

        private void TelnetRecvWorkFunc(object? obj)
        {
            var lastCheck = DateTime.Now;
            var buffer = new byte[4096];
            var index = 0;
            while (!_shutdown)
            {
                try
                {
                    if (Client is null)
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    if (!Client.Connected)
                    {
                        throw new Exception();
                    }
                    if (Client.Available > 0)
                    {
                        while (Client.Available > 0)
                        {
                            buffer[index] = (byte)Client.GetStream().ReadByte();

                            if (buffer[index] == '\r')
                            {
                                var str = Encoding.ASCII.GetString(buffer, 0, index);
                                if (str.StartsWith("@@"))
                                {
                                    _statusMessages.Enqueue(str.Trim());
                                }
                                else
                                {
                                    _commandResponseMessages.Enqueue(str);
                                }
                                index = 0;
                            }

                            index += 1;
                        }
                    }
                    else
                    {
                        if (lastCheck + TimeSpan.FromSeconds(30) < DateTime.Now)
                        {
                            SendCommand("NOP");
                        }
                        Thread.Sleep(1);
                    }
                }
                catch (Exception e)
                {
                    Client = null;
                    Disconnected?.Invoke(null, EventArgs.Empty);
                }
            }
        }

        public string Name { get; set; }
        public ushort Port { get; set; }
        public IPAddress Address { get; set; }
        private Thread TelnetRecvThread { get; set; }
        private Thread ProcessStatusThread { get; set; }
        private Thread KeepRemotePortOpenThread { get; set; }
        private TcpClient? Client { get; set; }
        public bool IsConnected => Client is not null && Client.Connected;

        private bool _shutdown;
        private readonly Queue<string> _commandResponseMessages = new();
        private readonly Queue<string> _statusMessages = new();

        private void SendCommand(string command)
        {
            var buffer = Encoding.ASCII.GetBytes($"#{command}\r\n");
            Client.GetStream().Write(buffer);
        }

        public void Shutdown()
        {
            _shutdown = true;
        }

        private string WaitForResponse()
        {
            while (_commandResponseMessages.Count == 0)
            {
                Thread.Sleep(1);
            }
            return _commandResponseMessages.Dequeue();
        }

        public (string Type, string Value) QueryDirectoryItem(int i)
        {
            SendCommand($"QDR {i}");

            var response = WaitForResponse();
            var match = QueryDirectoryResponse.Match(response);
            if (match.Success)
            {
                return (match.Groups["Type"].Value, match.Groups["Value"].Value);
            }
            else
            {
                match = OKResponse.Match(response);
                if (!match.Success)
                {
                    Debugger.Break();
                }
            }

            return ("", "");
        }

        public int QueryDirectorySize()
        {
            SendCommand("QDS");

            var response = WaitForResponse();
            var match = NumberResponse.Match(response);
            if (match.Success)
            {
                return int.Parse(match.Groups["Num"].Value);
            }
            else
            {
                Debugger.Break();
            }

            return -1;
        }

        public int QueryVerbosityMode()
        {
            SendCommand("QVM");

            var response = WaitForResponse();
            var match = NumberResponse.Match(response);
            if (match.Success)
            {
                return int.Parse(match.Groups["Num"].Value);
            }
            else
            {
                Debugger.Break();
            }

            return -1;
        }

        public int SetVerbosityMode(int i)
        {
            SendCommand($"SVM {i}");

            var response = WaitForResponse().Replace("@SVM ", String.Empty);
            var match = NumberResponse.Match(response);
            if (match.Success)
            {
                return int.Parse(match.Groups["Num"].Value);
            }
            else
            {
                Debugger.Break();
            }

            return -1;
        }

        private static Dictionary<RemoteKey, string> ButtonCommandMapping = new()
        {
            {RemoteKey.UpArrow,"NUP"},
            {RemoteKey.DownArrow,"NDN"},
            {RemoteKey.LeftArrow,"NLT"},
            {RemoteKey.RightArrow,"NRT"},
            {RemoteKey.Enter,"SEL"},
            {RemoteKey.Return,"RET"},
            {RemoteKey.PageUp,"PUP"},
            {RemoteKey.PageDown,"PDN"},
            {RemoteKey.Home,"HOM"},
            {RemoteKey.Stop,"STP"},
            {RemoteKey.Power, "POW"},
            {RemoteKey.Open, "EJT"},
            {RemoteKey.On, "PON"},
            {RemoteKey.Off, "POF"},
            {RemoteKey.Dimmer, "DIM"},
            {RemoteKey.PureAudio, "PUR"},
            {RemoteKey.VolInc, "VUP"},
            {RemoteKey.VolDec, "VDN"},
            {RemoteKey.Mute, "MUT"},
            {RemoteKey.Num1, "NU1"},
            {RemoteKey.Num2, "NU2"},
            {RemoteKey.Num3, "NU3"},
            {RemoteKey.Num4, "NU4"},
            {RemoteKey.Num5, "NU5"},
            {RemoteKey.Num6, "NU6"},
            {RemoteKey.Num7, "NU7"},
            {RemoteKey.Num8, "NU8"},
            {RemoteKey.Num9, "NU9"},
            {RemoteKey.Num0, "NU0"},
            {RemoteKey.Clear, "CLR"},
            {RemoteKey.Goto, "GOT"},
            {RemoteKey.Info, "OSD"},
            {RemoteKey.TopMenu, "TTL"},
            {RemoteKey.PopUpMenu, "MNU"},
            {RemoteKey.Setup, "SET"},
            {RemoteKey.Red, "RED"},
            {RemoteKey.Green, "GRN"},
            {RemoteKey.Blue, "BLU"},
            {RemoteKey.Yellow, "YLW"},
            {RemoteKey.Play, "PLA"},
            {RemoteKey.Pause, "PAU"},
            {RemoteKey.SkipPrev, "PRE"},
            {RemoteKey.Rev, "REV"},
            {RemoteKey.Fwd, "FWD"},
            {RemoteKey.SkipNext, "NXT"},
            {RemoteKey.Audio, "AUD"},
            {RemoteKey.Subtitle, "SUB"},
            {RemoteKey.Angle, "ANG"},
            {RemoteKey.Zoom, "ZOM"},
            {RemoteKey.SAP, "SAP"},
            {RemoteKey.ABReplay, "ATB"},
            {RemoteKey.Repeat, "RPT"},
            {RemoteKey.PIP, "PIP"},
            {RemoteKey.Resolution, "HDM"},
            {RemoteKey.SubtitleHold, "SUH"},
            {RemoteKey.Option, "OPT"},
            {RemoteKey._3D, "M3D"},
            {RemoteKey.Pic, "SEH"},
            {RemoteKey.HDR, "HDR"},
            {RemoteKey.InfoHold, "INH"},
            {RemoteKey.ResolutionHold, "RLH"},
            {RemoteKey.ShowAVSync, "AVS"},
            {RemoteKey.GaplessPlayback, "GPA"},
            {RemoteKey.Input, "SRC"},
        };

        public void RemoteCommand(RemoteKey key)
        {
            SendCommand(ButtonCommandMapping[key]);

            var response = WaitForResponse();
            var match = OKResponse.Match(response);
            if (!match.Success)
            {
                Debugger.Break();
            }
        }

        public void RemoteCommand(IList<RemoteKey> keys, int delayPerCommand = 200)
        {
            foreach (var key in keys)
            {
                RemoteCommand(key);
                Thread.Sleep(delayPerCommand);
            }
        }

        public void PlayBDMVFolder(string deviceDirPathToPlay)
        {
            var cmd = $"checkfolderhasbdmv?{{\"folderpath\":\"{deviceDirPathToPlay}\"}}";
            var response = PostHTTP(cmd);
        }

        private HttpResponseMessage PostHTTP(string command)
        {
            var myHttpClient = new HttpClient();
            return myHttpClient.GetAsync($"http://{Address.ToString()}:436/{command}").Result;
        }
    }
}