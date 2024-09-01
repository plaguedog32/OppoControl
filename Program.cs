using System.Data.SQLite;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using OppoControl;

namespace OppoTelnet
{
    internal class Program
    {
        private static HttpListener plexListener = new();
        private static Queue<string> PlexJsonQueue = new();
        private static readonly ConfigurationData _config = ConfigurationData.LoadData() ?? throw new Exception("Failed to load config data");

        static void Main(string[] args)
        {
            var unit = new OppoUnit();
            unit.StatusEventHandler += (_, e) =>
            {
                if (e.Message == "@@UPL STOP")
                {
                    Console.WriteLine("Oppo Stopped Playback, switching input to Plex Device");
                    var myHttpClient = new HttpClient();
                    var result = myHttpClient.GetAsync($"http://{_config.HTP1Ip}/ircmd?code={_config.PlexDeviceInputRemoteCode}").Result;
                }
                else if (e.Message == "@@UPL PLAY")
                {
                    Console.WriteLine("Oppo Started Playback, switching input to Oppo");
                    var myHttpClient = new HttpClient();
                    var result = myHttpClient.GetAsync($"http://{_config.HTP1Ip}/ircmd?code={_config.OppoInputRemoteCode}").Result;
                }
            };
            unit.Disconnected += (_, _) =>
            {
                Console.WriteLine("Oppo disconnected from telnet");
                unit.Connect();
            };

            var bdmvFolders = new Dictionary<string, Dictionary<string, DirectoryInfo>>();
            var bdmvCleanup = new Regex(@"^(\d+ - )", RegexOptions.Compiled);
            var searching = false;

            unit.Connected += (_, e) =>
            {
                searching = true;
                bdmvFolders.Clear();
                foreach (var share in _config.ShareRoots)
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

            Console.WriteLine("listening for plex");
            var plexListenerThread = new Thread(() =>
            {
                plexListener.Prefixes.Add($"http://*:{_config.PlexWebhookPort}/");

                try
                {
                    plexListener.Start();
                }
                catch
                {
                    Console.Error.WriteLine("Must run as admin");
                    throw;
                }

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

            var connection = new SQLiteConnection($"Data Source={_config.PlexDBPath};Read Only=True;");
            connection.Open();

            while (true)
            {
                if (searching || PlexJsonQueue.Count <= 0)
                {
                    Thread.Sleep(1);
                    continue;
                }

                var json = PlexJsonQueue.Dequeue();
                var data = JsonSerializer.Deserialize<Rootobject>(json);

                if (data.Event != "media.play" || !unit.IsConnected)
                {
                    continue;
                }

                foreach (var share in _config.ShareRoots)
                {
                    if (!data.Player.local || data.Metadata.librarySectionTitle != share.PlexBDMVLibraryName)
                    {
                        continue;
                    }

                    var library = bdmvFolders[share.PlexBDMVLibraryName];
                    var getFilePath = $"SELECT b.file FROM media_items a INNER JOIN media_parts b ON a.id=b.media_item_id WHERE a.metadata_item_id={data.Metadata.key.Split("/").Last()}";

                    var command = connection.CreateCommand();
                    command.CommandText = getFilePath;
                    var reader = command.ExecuteReader();

                    if (!reader.Read())
                    {
                        continue;
                    }

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