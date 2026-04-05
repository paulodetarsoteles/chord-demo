using System;
using System.IO;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Linq;

namespace ChordApi.Services
{
    public static class StorageService
    {
        public static void CleanStorage(string storageDir)
        {
            if (string.IsNullOrEmpty(storageDir)) return;

            if (Directory.Exists(storageDir))
            {
                try
                {
                    var files = Directory.GetFiles(storageDir);

                    foreach (var f in files) File.Delete(f);

                    var dirs = Directory.GetDirectories(storageDir);

                    foreach (var d in dirs) Directory.Delete(d, true);

                    Console.WriteLine($"Storage cleaned on startup: {files.Length} files, {dirs.Length} directories removed.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Warning: unable to clean storage on startup: " + ex.Message);
                }
            }
            else
            {
                Directory.CreateDirectory(storageDir);
            }
        }

        public static void EnsureStorage(string storageDir)
        {
            if (string.IsNullOrEmpty(storageDir)) return;
            if (!Directory.Exists(storageDir)) Directory.CreateDirectory(storageDir);
        }

        public static (string id, string filePath) SaveUploadedFile(IFormFile file, string storageDir)
        {
            // preserve original filename (sanitized) and avoid collisions by appending a numeric suffix
            var originalName = Path.GetFileName(file.FileName) ?? "upload";
            var ext = Path.GetExtension(originalName);
            var baseName = Path.GetFileNameWithoutExtension(originalName);
            var safeBase = SanitizeFileName(baseName);

            var candidate = safeBase + ext;
            var filePath = Path.Combine(storageDir, candidate);
            int i = 1;

            while (File.Exists(filePath))
            {
                candidate = safeBase + "-" + i + ext;
                filePath = Path.Combine(storageDir, candidate);
                i++;
            }

            using (var fs = File.Create(filePath))
            {
                file.CopyTo(fs);
            }

            var id = Path.GetFileNameWithoutExtension(candidate);

            return (id, filePath);
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            // trim and limit length
            name = name.Trim();
            if (name.Length > 100) name = name.Substring(0, 100);
            if (string.IsNullOrWhiteSpace(name)) name = "upload";

            return name;
        }

        public static void SaveTimelineJson(string id, string timelineJson, string storageDir)
        {
            var timelinePath = Path.Combine(storageDir, id + ".json");
            File.WriteAllText(timelinePath, timelineJson);
        }

        public static Dictionary<string, (string filePath, string timelineJson)> LoadExistingStore(string storageDir)
        {
            var store = new Dictionary<string, (string filePath, string timelineJson)>();

            if (!Directory.Exists(storageDir)) return store;

            var audioExts = new[] { ".mp3", ".wav", ".m4a", ".flac" };

            foreach (var f in Directory.GetFiles(storageDir))
            {
                var ext = Path.GetExtension(f).ToLower();

                if (!audioExts.Contains(ext)) continue;

                var id = Path.GetFileNameWithoutExtension(f);
                var jsonPath = Path.Combine(storageDir, id + ".json");
                string timelineJson = null;

                if (File.Exists(jsonPath)) timelineJson = File.ReadAllText(jsonPath);
                
                store[id] = (f, timelineJson ?? "{}");
            }

            return store;
        }

        public static string GetContentType(string path)
        {
            return Path.GetExtension(path).ToLower() switch
            {
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                _ => "application/octet-stream"
            };
        }
    }
}
