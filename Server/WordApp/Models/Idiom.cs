namespace WordApp.Models
{
    public class Idiom
    {
        public string Id { get; set; } = null!;
        public string Title { get; set; } = "";
        public string Category { get; set; } = "";
        public string Example { get; set; } = "";
        public string Explanation { get; set; } = "";
        public string Note { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
