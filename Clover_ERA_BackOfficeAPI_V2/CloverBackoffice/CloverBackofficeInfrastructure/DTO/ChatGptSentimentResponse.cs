using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class ChatGptSentimentResponse
    {
        // Each category like "Communication", "Learning" etc.
        public Dictionary<string, SentimentBreakdown> Categories { get; set; } = new();
        public List<string> TopThemes { get; set; } = new();
        public List<string> TopRecommendations { get; set; } = new();
        public List<string> TopActions { get; set; } = new();

        // Add this new property
        public SuggestedWorksheetDto? SuggestedWorksheet { get; set; }

        //  Add this method inside the class
        public void Combine(ChatGptSentimentResponse other)
        {
            if (other == null) return;

            // Merge Categories
            foreach (var kvp in other.Categories)
            {
                if (Categories.ContainsKey(kvp.Key))
                {
                    var current = Categories[kvp.Key];
                    var incoming = kvp.Value;

                    current.Angry += incoming.Angry;
                    current.Sad += incoming.Sad;
                    current.Indifferent += incoming.Indifferent;
                    current.Happy += incoming.Happy;
                }
                else
                {
                    Categories[kvp.Key] = new SentimentBreakdown
                    {
                        Angry = kvp.Value.Angry,
                        Sad = kvp.Value.Sad,
                        Indifferent = kvp.Value.Indifferent,
                        Happy = kvp.Value.Happy
                    };
                }
            }

            // Merge TopThemes and TopRecommendations (deduplicated)
            if (other.TopThemes != null)
            {
                TopThemes.AddRange(other.TopThemes.Where(t => !TopThemes.Contains(t)));
            }

            if (other.TopRecommendations != null)
            {
                TopRecommendations.AddRange(other.TopRecommendations.Where(r => !TopRecommendations.Contains(r)));
            }
            //  Merge TopActions (deduplicated)
            if (other.TopActions != null)
            {
                TopActions.AddRange(other.TopActions.Where(a => !TopActions.Contains(a)));
            }
        }
    }
    public class SentimentBreakdown
    {
        public double Angry { get; set; }
        public double Sad { get; set; }
        public double Indifferent { get; set; }
        public double Happy { get; set; }
    }
    // Add this nested DTO
    public class SuggestedWorksheetDto
    {
        public string? Category { get; set; }
        public string? FileName { get; set; }
    }

}
