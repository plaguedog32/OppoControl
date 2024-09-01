using System.Data.SQLite;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using OppoControl;

namespace OppoTelnet
{
    public class ConfigurationData
    {
        public string HTP1Ip { get; set; }
        public ShareRoot[] ShareRoots { get; set; }
        public string PlexWebhookPort { get; set; }
        public string PlexDBPath { get; set; }
        public string PlexDeviceInputRemoteCode { get; set; }
        public string OppoInputRemoteCode { get; set; }
    }

    public class ShareRoot
    {
        public string Root { get; set; }
        public string PlexBDMVLibraryRoot { get; set; }
        public string PlexBDMVLibraryName { get; set; }
        public string OppoBDMVShareRoot { get; set; }
        public string QualityTag { get; set; }
    }

    internal class Program
    {
        private static HttpListener plexListener = new();
        private static Queue<string> PlexJsonQueue = new();
        private static ConfigurationData Data;

        static void Main(string[] args)
        {
            Data = JsonSerializer.Deserialize<ConfigurationData>(File.ReadAllText("Configuration.json"));

            var unit = new OppoUnit();
            unit.StatusEventHandler += (_, e) =>
            {
                if (e.Message == "@@UPL STOP")
                {
                    Console.WriteLine("Oppo Stopped Playback, switching input to Plex Device");
                    var myHttpClient = new HttpClient();
                    var result = myHttpClient.GetAsync($"http://{Data.HTP1Ip}/ircmd?code={Data.PlexDeviceInputRemoteCode}").Result;
                }
                else if (e.Message == "@@UPL PLAY")
                {
                    Console.WriteLine("Oppo Started Playback, switching input to Oppo");
                    var myHttpClient = new HttpClient();
                    var result = myHttpClient.GetAsync($"http://{Data.HTP1Ip}/ircmd?code={Data.OppoInputRemoteCode}").Result;
                }
            };

            var bdmvFolders = new Dictionary<string, Dictionary<string, DirectoryInfo>>();
            var bdmvCleanup = new Regex(@"^(\d+ - )", RegexOptions.Compiled);
            var searching = false;

            unit.Connected += (_, e) =>
            {
                searching = true;
                bdmvFolders.Clear();
                foreach (var share in Data.ShareRoots)
                {
                    var library = new Dictionary<string, DirectoryInfo>();
                    bdmvFolders.Add(share.PlexBDMVLibraryName, library);
                    var uhdRoot = new DirectoryInfo(share.Root);
                    var plexFiles = new DirectoryInfo(share.PlexBDMVLibraryRoot).GetFiles("*.mp4");

                    var searchQueue = new Queue<DirectoryInfo>();
                    searchQueue.Enqueue(uhdRoot);

                    while (searchQueue.Count > 0)
                    {
                        var dir = searchQueue.Dequeue();

                        if (Directory.Exists(Path.Combine(dir.FullName, "BDMV")))
                        {
                            if (!dir.Name.Contains(share.QualityTag))
                            {
                                var cleanName = dir.Parent.Name.Replace($" {share.QualityTag}", String.Empty);
                                var match = bdmvCleanup.Match(cleanName);
                                if (match.Success)
                                {
                                    var replace = match.Groups[0].Value;
                                    cleanName = cleanName.Replace(replace, String.Empty);
                                }
                                if (!library.ContainsKey(cleanName))
                                {
                                    library.Add(cleanName, dir);
                                }
                            }
                            else
                            {
                                var cleanName = dir.Name.Replace($" {share.QualityTag}", String.Empty);

                                var match = bdmvCleanup.Match(cleanName);
                                if (match.Success)
                                {
                                    var replace = match.Groups[0].Value;
                                    cleanName = cleanName.Replace(replace, String.Empty);
                                }

                                library.Add(cleanName, dir);
                            }
                            continue;
                        }

                        foreach (var subDir in dir.GetDirectories())
                        {
                            searchQueue.Enqueue(subDir);
                        }
                    }

                    foreach (var file in plexFiles)
                    {
                        var name = file.Name.Replace(".mp4", String.Empty);

                        if (!library.ContainsKey(name))
                        {
                            var matches = library.Keys.Where(k => k.Contains(name)).ToArray();
                            Debugger.Break();
                        }
                    }
                }

                searching = false;
            };

            Console.WriteLine("Scan files");
            unit.Connect();

            unit.SetVerbosityMode(3);
            Console.WriteLine("listening for plex");
            var plexListenerThread = new Thread(() =>
            {
                plexListener.Prefixes.Add($"http://*:{Data.PlexWebhookPort}/");

                plexListener.Start();

                while (true)
                {
                    HttpListenerContext context = plexListener.GetContext();
                    if (context.Request.ContentType == "application/json" || context.Request.ContentType.Contains("multipart/form-data"))
                    {
                        using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding, true, -1, false))
                        {
                            var lines = new List<string>();
                            var line = reader.ReadLine();
                            while (true)
                            {
                                line = reader.ReadLine();
                                if (line.StartsWith("--------------------------"))
                                {
                                    break;
                                }
                                lines.Add(line);
                            }
                            PlexJsonQueue.Enqueue(lines.Last());
                        }
                    }
                    else
                    {
                        using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding, true, -1, false))
                        {
                            var line = reader.ReadToEnd();
                            Debugger.Break();
                        }
                    }

                    using (HttpListenerResponse resp = context.Response)
                    {
                        resp.StatusCode = (int)HttpStatusCode.OK;
                        resp.StatusDescription = "Status OK";
                    }
                }
            });

            plexListenerThread.Start();

            var connection = new SQLiteConnection($"Data Source={Data.PlexDBPath};Read Only=True;");
            connection.Open();

            while (true)
            {
                if (searching)
                {
                    Thread.Sleep(1);
                    continue;
                }

                if (PlexJsonQueue.Count > 0)
                {
                    var json = PlexJsonQueue.Dequeue();

                    var data = JsonSerializer.Deserialize<Rootobject>(json);

                    if (data.Event == "media.play" && unit.IsConnected)
                    {
                        foreach (var share in Data.ShareRoots)
                        {
                            if (data.Player.local && data.Metadata.librarySectionTitle == share.PlexBDMVLibraryName)
                            {
                                var library = bdmvFolders[share.PlexBDMVLibraryName];
                                var getFilePath = $"SELECT b.file FROM media_items a INNER JOIN media_parts b ON a.id=b.media_item_id WHERE a.metadata_item_id={data.Metadata.key.Split("/").Last()}";

                                var command = connection.CreateCommand();
                                command.CommandText = getFilePath;
                                var reader = command.ExecuteReader();

                                if (reader.Read())
                                {
                                    var filePathResult = reader.GetString(0);
                                    var fileName = Path.GetFileName(filePathResult).Replace(".mp4", string.Empty);
                                    if (library.ContainsKey(fileName))
                                    {
                                        var bdmvFolder = library[fileName];
                                        unit.PlayBDMVFolder($"{share.OppoBDMVShareRoot}/{bdmvFolder.FullName.Substring(share.Root.Length + 1).Replace(@"\", "/")}");

                                        Console.WriteLine($"Playing {data.Metadata.title}");
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
        }
    }
}