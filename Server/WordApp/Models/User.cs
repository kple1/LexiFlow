using System.Text.Json.Serialization;

namespace WordApp.Models
{
    public class User
    {
        public int Id { get; set; }
        public string UserId { get; set; } = "";
        [JsonIgnore]
        public string Pw { get; set; } = "";
    }
}
