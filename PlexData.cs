using System.Text.Json.Serialization;

namespace OppoTelnet
{
    public class Rootobject
    {
        [JsonPropertyName("event")]
        public string Event { get; set; }
        public bool user { get; set; }
        public bool owner { get; set; }
        public Account Account { get; set; }
        public Server Server { get; set; }
        public Player Player { get; set; }
        public Metadata Metadata { get; set; }
    }

    public class Account
    {
        public int id { get; set; }
        public string thumb { get; set; }
        public string title { get; set; }
    }

    public class Server
    {
        public string title { get; set; }
        public string uuid { get; set; }
    }

    public class Player
    {
        public bool local { get; set; }
        public string publicAddress { get; set; }
        public string title { get; set; }
        public string uuid { get; set; }
    }

    public class Metadata
    {
        public string librarySectionType { get; set; }
        public string ratingKey { get; set; }
        public string key { get; set; }
        public string guid { get; set; }
        public string slug { get; set; }
        public string studio { get; set; }
        public string type { get; set; }
        public string title { get; set; }
        public string titleSort { get; set; }
        public string librarySectionTitle { get; set; }
        public int librarySectionID { get; set; }
        public string librarySectionKey { get; set; }
        public string contentRating { get; set; }
        public string summary { get; set; }
        public float rating { get; set; }
        public float audienceRating { get; set; }
        public int year { get; set; }
        public string tagline { get; set; }
        public string thumb { get; set; }
        public string art { get; set; }
        public int duration { get; set; }
        public string originallyAvailableAt { get; set; }
        public int addedAt { get; set; }
        public int updatedAt { get; set; }
        public string audienceRatingImage { get; set; }
        public string primaryExtraKey { get; set; }
        public string ratingImage { get; set; }
        public Ultrablurcolors UltraBlurColors { get; set; }
        public Genre[] Genre { get; set; }
        public Country[] Country { get; set; }
        public Guid[] Guid { get; set; }
        public Rating[] Rating { get; set; }
        public Director[] Director { get; set; }
        public Writer[] Writer { get; set; }
        public Role[] Role { get; set; }
        public Producer[] Producer { get; set; }
    }

    public class Ultrablurcolors
    {
        public string topLeft { get; set; }
        public string topRight { get; set; }
        public string bottomRight { get; set; }
        public string bottomLeft { get; set; }
    }

    public class Genre
    {
        public int id { get; set; }
        public string filter { get; set; }
        public string tag { get; set; }
        public int count { get; set; }
    }

    public class Country
    {
        public int id { get; set; }
        public string filter { get; set; }
        public string tag { get; set; }
        public int count { get; set; }
    }

    public class Guid
    {
        public string id { get; set; }
    }

    public class Rating
    {
        public string image { get; set; }
        public float value { get; set; }
        public string type { get; set; }
        public int count { get; set; }
    }

    public class Director
    {
        public int id { get; set; }
        public string filter { get; set; }
        public string tag { get; set; }
        public string tagKey { get; set; }
        public int count { get; set; }
        public string thumb { get; set; }
    }

    public class Writer
    {
        public int id { get; set; }
        public string filter { get; set; }
        public string tag { get; set; }
        public string tagKey { get; set; }
        public int count { get; set; }
        public string thumb { get; set; }
    }

    public class Role
    {
        public int id { get; set; }
        public string filter { get; set; }
        public string tag { get; set; }
        public string tagKey { get; set; }
        public int count { get; set; }
        public string role { get; set; }
        public string thumb { get; set; }
    }

    public class Producer
    {
        public int id { get; set; }
        public string filter { get; set; }
        public string tag { get; set; }
        public string tagKey { get; set; }
        public int count { get; set; }
        public string thumb { get; set; }
    }
}