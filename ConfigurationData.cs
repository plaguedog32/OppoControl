using System.Text.Json;

namespace OppoControl
{
    public class ConfigurationData
    {
        public static ConfigurationData? LoadData()
        {
            return JsonSerializer.Deserialize<ConfigurationData>(File.ReadAllText("Configuration.json"));
        }

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
}