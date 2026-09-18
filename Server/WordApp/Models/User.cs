using System.Text.Json.Serialization;

namespace WordApp.Models
{
    public class User
    {
        public int Id { get; set; }
        public string UserId { get; set; } = "";
        [JsonIgnore]
        public string Pw { get; set; } = "";
        [JsonIgnore]
        public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
        [JsonIgnore]
        public int FailedLoginCount { get; set; }
        [JsonIgnore]
        public DateTime? LockoutUntil { get; set; }
    }
}
