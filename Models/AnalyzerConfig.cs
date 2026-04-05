using System;

namespace ChordApi.Models
{
    public class AnalyzerConfig
    {
        public int TargetRate { get; set; } = 22050;
        public int FftSize { get; set; } = 2048;
        public int Hop { get; set; } = 512;

        // band cutoffs and weights
        public int BassCutoff { get; set; } = 800; // Hz — frequencies >= BassCutoff used for chromaHigh

        // mid-range attenuation (to reduce voice influence)
        public int MidCutLow { get; set; } = 1000; // Hz
        public int MidCutHigh { get; set; } = 2000; // Hz
        public double MidAttenuation { get; set; } = 0.2; // 0.0..1.0

        // high frequency attenuation
        public int HighFreqCutoff { get; set; } = 4000; // Hz
        public double HighFreqAttenuation { get; set; } = 0.2; // 0.0..1.0

        // weights in final chroma combination
        public double HighWeight { get; set; } = 0.3;
        public double FullWeight { get; set; } = 0.3;

        // smoothing window length (odd recommended)
        public int SmoothingWindow { get; set; } = 7;

        // minimum duration (seconds) for a timeline segment; segments shorter than
        // this will be merged into neighbors to reduce transient label flicker.
        public double MinSegmentDurationSeconds { get; set; } = 0.25;
    }
}
