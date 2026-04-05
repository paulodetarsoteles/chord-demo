using System.Collections.Generic;

namespace ChordApi.Models
{
    public class Segment
    {
        public double Start { get; set; }
        public double End { get; set; }
        public string Label { get; set; } = string.Empty;
        public double Confidence { get; set; }
    }

    public class TimelineResult
    {
        public string File { get; set; } = string.Empty;
        public double Duration { get; set; }
        public List<Segment> Timeline { get; set; } = new List<Segment>();
    }
}
