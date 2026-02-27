namespace Changsta.Ai.Infrastructure.Services.SoundCloud.Models
{
    public class MixSchema
    {
        public string? Genre { get; set; }

        public string? Energy { get; set; }

        public int? BpmMin { get; set; }

        public int? BpmMax { get; set; }

        public IReadOnlyList<string> Moods { get; set; } = new List<string>();
    }
}