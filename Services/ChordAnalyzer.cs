using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using System.Numerics;
using NAudio.Wave;
using ChordApi.Models;

namespace ChordApi.Services
{
    public static class ChordAnalyzer
    {
        private static readonly double[][] Templates;
        private static readonly string[] Labels;

        static ChordAnalyzer()
        {
            var names = new[] { "C","C#","D","D#","E","F","F#","G","G#","A","A#","B" };
            var major = Enumerable.Range(0,12).Select(i => (i==0||i==4||i==7)?1.0:0.0).ToArray();
            var minor = Enumerable.Range(0,12).Select(i => (i==0||i==3||i==7)?1.0:0.0).ToArray();
            var templates = new List<double[]>();
            var labels = new List<string>();

            for (int root=0; root<12; root++)
            {
                templates.Add(Roll(major, root));
                labels.Add(names[root] + "");
                templates.Add(Roll(minor, root));
                labels.Add(names[root] + "m");
            }

            Templates = templates.Select(t => Normalize(t)).ToArray();
            Labels = labels.ToArray();
        }

        private static double[] Roll(double[] arr, int shift)
        {
            var n = arr.Length;
            var outArr = new double[n];

            for (int i=0;i<n;i++) outArr[i] = arr[(i - shift + n) % n];

            return outArr;
        }

        private static double[] Normalize(double[] v)
        {
            var norm = Math.Sqrt(v.Select(x=>x*x).Sum());

            if (norm == 0) return v;

            return v.Select(x => x / norm).ToArray();
        }

        public static TimelineResult AnalyzeFile(string path, AnalyzerConfig cfg)
        {
            // Ensure config defaults if null
            if (cfg == null) cfg = new AnalyzerConfig();

            int targetRate = cfg.TargetRate;
            int fftSize = cfg.FftSize;
            int hop = cfg.Hop;

            float[] samples;
            int sampleRate;

            using (var afr = new AudioFileReader(path))
            {
                var inputSampleRate = afr.WaveFormat.SampleRate;
                var channels = afr.WaveFormat.Channels;

                ISampleProvider provider = afr.ToSampleProvider();

                if (inputSampleRate != targetRate)
                {
                    var newFormat = new WaveFormat(targetRate, channels);
                    var resampler = new MediaFoundationResampler(afr, newFormat) { ResamplerQuality = 60 };
                    provider = resampler.ToSampleProvider();
                }

                var buffer = new List<float>();
                var readBuffer = new float[4096];
                int read;

                while ((read = provider.Read(readBuffer, 0, readBuffer.Length)) > 0)
                {
                    if (channels == 1)
                    {
                        for (int i=0;i<read;i++) buffer.Add(readBuffer[i]);
                    }
                    else
                    {
                        for (int i=0;i<read;i+=channels)
                        {
                            float sum = 0;

                            for (int c=0;c<channels;c++) sum += readBuffer[i+c];

                            buffer.Add(sum / channels);
                        }
                    }
                }

                samples = buffer.ToArray();
                sampleRate = targetRate;
            }

            var duration = (double)samples.Length / sampleRate;
            var window = new double[fftSize];

            for (int i = 0; i < fftSize; i++) window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (fftSize - 1)); // Hann

            var magnitudes = new List<float[]>();

            for (int pos = 0; pos + 1 <= samples.Length; pos += hop)
            {
                var frame = new double[fftSize];

                for (int i = 0; i < fftSize; i++)
                {
                    var idx = pos + i;
                    frame[i] = (idx < samples.Length) ? samples[idx] * window[i] : 0.0;
                }

                var complex = new Complex[fftSize];

                for (int i = 0; i < fftSize; i++) complex[i] = new Complex(frame[i], 0);

                Fourier.Forward(complex, FourierOptions.Matlab);

                var spectrum = new float[fftSize / 2 + 1];

                for (int k = 0; k < spectrum.Length; k++) spectrum[k] = (float)complex[k].Magnitude;

                magnitudes.Add(spectrum);
            }

            var chromaList = new List<double[]>();

            // --- TUNABLE: band cutoffs and weights read from config ---
            int bassCutoff = cfg.BassCutoff;
            int midCutLow = cfg.MidCutLow;
            int midCutHigh = cfg.MidCutHigh;
            double midAttenuation = cfg.MidAttenuation;
            int highFreqCutoff = cfg.HighFreqCutoff;
            double highFreqAttenuation = cfg.HighFreqAttenuation;
            double highWeight = cfg.HighWeight;
            double fullWeight = cfg.FullWeight;

            for (int f = 0; f < magnitudes.Count; f++)
            {
                var mag = magnitudes[f];
                var chromaFull = new double[12];
                var chromaHigh = new double[12];

                for (int bin = 0; bin < mag.Length; bin++)
                {
                    var freq = (double)bin * sampleRate / fftSize;

                    if (freq < 50) continue;
                    
                    var midi = 69 + 12 * Math.Log(freq/440.0, 2);
                    var pitch = (int)Math.Round(midi) % 12;

                    if (pitch < 0) pitch = (pitch + 12) % 12;

                    double val = mag[bin];

                    // Atenuação de faixa média (voz geralmente entre ~250Hz e ~3kHz)
                    if (freq >= midCutLow && freq <= midCutHigh) val *= midAttenuation;
                    
                    // Atenuação de agudos muito altos, se desejado
                    if (freq >= highFreqCutoff) val *= highFreqAttenuation;

                    chromaFull[pitch] += val;

                    if (freq >= bassCutoff) chromaHigh[pitch] += val;
                }

                var normFull = Math.Sqrt(chromaFull.Select(x=>x*x).Sum()) + 1e-8;
                var normHigh = Math.Sqrt(chromaHigh.Select(x=>x*x).Sum()) + 1e-8;

                var finalChroma = new double[12];

                for (int i=0;i<12;i++)
                {
                    finalChroma[i] = fullWeight * (chromaFull[i] / normFull) + highWeight * (chromaHigh[i] / normHigh);
                }

                var normFinal = Math.Sqrt(finalChroma.Select(x=>x*x).Sum()) + 1e-8;
                
                for (int i=0;i<12;i++) finalChroma[i] /= normFinal;

                chromaList.Add(finalChroma);
            }

            // compare to templates
            var labels = new List<string>();
            var confs = new List<double>();

            foreach (var c in chromaList)
            {
                double best = double.NegativeInfinity;
                int bestIdx = 0;

                for (int t=0;t<Templates.Length;t++)
                {
                    var dot = 0.0;

                    for (int i=0;i<12;i++) dot += Templates[t][i] * c[i];

                    if (dot > best) { best = dot; bestIdx = t; }
                }

                labels.Add(Labels[bestIdx]);
                confs.Add(best);
            }

            // smoothing majority window
            var smooth = SmoothLabels(labels, cfg.SmoothingWindow);

            // build timeline (with minimum-segment-duration merging)
            var timeline = BuildTimeline(smooth, confs, hop / (double)sampleRate, cfg.MinSegmentDurationSeconds);

            return new TimelineResult
            {
                File = Path.GetFileName(path),
                Duration = duration,
                Timeline = timeline
            };
        }

        private static List<Segment> BuildTimeline(List<string> labels, List<double> confs, double frameDuration, double minSegmentDurationSeconds)
        {
            var outList = new List<Segment>();

            if (labels.Count == 0) return outList;

            var cur = labels[0];
            var start = 0.0;
            var confAccum = new List<double> { confs[0] };

            for (int i=1;i<labels.Count;i++)
            {
                if (labels[i] != cur)
                {
                    var end = i * frameDuration;
                    outList.Add(new Segment { Start = Math.Round(start,3), End = Math.Round(end,3), Label = cur, Confidence = confAccum.Average() });
                    cur = labels[i];
                    start = i * frameDuration;
                    confAccum = new List<double> { confs[i] };
                }
                else
                {
                    confAccum.Add(confs[i]);
                }
            }

            outList.Add(new Segment { Start = Math.Round(start,3), End = Math.Round(labels.Count * frameDuration,3), Label = cur, Confidence = confAccum.Average() });
            
            // If no merging requested or threshold <= 0, return as-is
            if (minSegmentDurationSeconds <= 0.0) return outList;

            // Post-process: merge segments shorter than threshold into neighbors.
            var merged = new List<Segment>();

            for (int i = 0; i < outList.Count; i++)
            {
                var seg = outList[i];
                var dur = seg.End - seg.Start;

                if (dur >= minSegmentDurationSeconds || merged.Count == 0 && i == 0 && outList.Count == 1)
                {
                    merged.Add(seg);
                    continue;
                }

                if (dur >= minSegmentDurationSeconds)
                {
                    merged.Add(seg);
                    continue;
                }

                // segment is short: decide to merge into previous or next
                var hasPrev = merged.Count > 0;
                var hasNext = i + 1 < outList.Count;

                if (!hasPrev && hasNext)
                {
                    // merge into next: extend next.Start back
                    var next = outList[i+1];
                    next.Start = seg.Start;
                    // recompute weighted confidence
                    var nextDur = next.End - next.Start;
                    next.Confidence = (next.Confidence * (nextDur - dur) + seg.Confidence * dur) / Math.Max(1e-9, nextDur);
                    outList[i+1] = next;
                }
                else if (hasPrev && !hasNext)
                {
                    // merge into previous
                    var prev = merged[merged.Count - 1];
                    var prevDur = prev.End - prev.Start;
                    prev.End = seg.End;
                    prev.Confidence = (prev.Confidence * prevDur + seg.Confidence * dur) / Math.Max(1e-9, prevDur + dur);
                    merged[merged.Count - 1] = prev;
                }
                else if (hasPrev && hasNext)
                {
                    var prev = merged[merged.Count - 1];
                    var next = outList[i+1];
                    var prevDur = prev.End - prev.Start;
                    var nextDur = next.End - next.Start;

                    // merge into longer neighbor
                    if (prevDur >= nextDur)
                    {
                        prev.End = seg.End;
                        prev.Confidence = (prev.Confidence * prevDur + seg.Confidence * dur) / Math.Max(1e-9, prevDur + dur);
                        merged[merged.Count - 1] = prev;
                    }
                    else
                    {
                        next.Start = seg.Start;
                        next.Confidence = (next.Confidence * (nextDur - dur) + seg.Confidence * dur) / Math.Max(1e-9, nextDur);
                        outList[i+1] = next;
                    }
                }
                else
                {
                    // fallback: append
                    merged.Add(seg);
                }
            }

            // Final pass: ensure rounding and non-overlap
            for (int i=0;i<merged.Count;i++)
            {
                merged[i].Start = Math.Round(merged[i].Start,3);
                merged[i].End = Math.Round(merged[i].End,3);
            }

            return merged;
        }

        private static List<string> SmoothLabels(List<string> labels, int window)
        {
            var n = labels.Count;
            var outL = new List<string>(labels);

            for (int i=0;i<n;i++)
            {
                var a = Math.Max(0, i - window/2);
                var b = Math.Min(n, i + window/2 + 1);
                var slice = labels.GetRange(a, b-a);
                var best = slice.GroupBy(x=>x).OrderByDescending(g=>g.Count()).First().Key;
                outL[i] = best;
            }
            
            return outL;
        }
    }
}
