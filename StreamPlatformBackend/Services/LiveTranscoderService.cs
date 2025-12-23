using Microsoft.Extensions.Logging;
using StreamPlatformBackend.Models.Stream;
using StreamPlatformBackend.Models.User;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace StreamPlatformBackend.Services
{
    public interface ILiveTranscoderService
    {
        Task StartAsync(StreamModel stream, UserModel user);
        Task StopAsync(int streamId);
        bool IsRunning(int streamId);
    }

    public class LiveTranscoderService : ILiveTranscoderService
    {
        private readonly ILogger<LiveTranscoderService> _logger;

        private const string FfmpegPath = "/usr/bin/ffmpeg";
        private const string LiveBasePath = "/var/www/streamplatform/live";
        private const string RtmpBaseUrl = "rtmp://127.0.0.1/live";

        // streamId -> ffmpeg process
        private static readonly ConcurrentDictionary<int, Process> _processes = new();

        public LiveTranscoderService(ILogger<LiveTranscoderService> logger)
        {
            _logger = logger;
        }

        public async Task StartAsync(StreamModel stream, UserModel user)
        {
            if (_processes.ContainsKey(stream.Id))
                return;

            var streamKey = user.StreamKey;
            var baseDir = Path.Combine(LiveBasePath, stream.PublicId);

            Directory.CreateDirectory(baseDir);
            Directory.CreateDirectory(Path.Combine(baseDir, "1080p"));
            Directory.CreateDirectory(Path.Combine(baseDir, "720p"));
            Directory.CreateDirectory(Path.Combine(baseDir, "480p"));

            var args = $@"
-hide_banner -loglevel warning
-i {RtmpBaseUrl}/{streamKey}

-map 0:v -map 0:a?
-c:v:0 libx264 -preset veryfast -tune zerolatency -s 1920x1080 -b:v:0 6000k
-c:a:0 aac -b:a:0 160k
-f hls -hls_time 2 -hls_list_size 10 -hls_flags delete_segments+append_list
-hls_segment_filename ""{baseDir}/1080p/index_%03d.ts""
""{baseDir}/1080p/index.m3u8""

-map 0:v -map 0:a?
-c:v:1 libx264 -preset veryfast -tune zerolatency -s 1280x720 -b:v:1 3000k
-c:a:1 aac -b:a:1 128k
-f hls -hls_time 2 -hls_list_size 10 -hls_flags delete_segments+append_list
-hls_segment_filename ""{baseDir}/720p/index_%03d.ts""
""{baseDir}/720p/index.m3u8""

-map 0:v -map 0:a?
-c:v:2 libx264 -preset veryfast -tune zerolatency -s 854x480 -b:v:2 1500k
-c:a:2 aac -b:a:2 96k
-f hls -hls_time 2 -hls_list_size 10 -hls_flags delete_segments+append_list
-hls_segment_filename ""{baseDir}/480p/index_%03d.ts""
""{baseDir}/480p/index.m3u8""
";

            var psi = new ProcessStartInfo
            {
                FileName = FfmpegPath,
                Arguments = args.Replace("\n", " "),
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = Process.Start(psi)
                ?? throw new Exception("Failed to start ffmpeg");

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    _logger.LogError("[ffmpeg:{StreamId}] {Line}", stream.Id, e.Data);
            };
            process.BeginErrorReadLine();

            _processes[stream.Id] = process;

            File.WriteAllText(Path.Combine(baseDir, "master.m3u8"),
        @"#EXTM3U
#EXT-X-VERSION:3
#EXT-X-STREAM-INF:BANDWIDTH=6500000,RESOLUTION=1920x1080
1080p/index.m3u8
#EXT-X-STREAM-INF:BANDWIDTH=3500000,RESOLUTION=1280x720
720p/index.m3u8
#EXT-X-STREAM-INF:BANDWIDTH=1800000,RESOLUTION=854x480
480p/index.m3u8
");

            _logger.LogInformation("HLS master playlist created for stream {StreamId}", stream.Id);
        }





        public async Task StopAsync(int streamId)
        {
            if (!_processes.TryRemove(streamId, out var process))
                return;

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to stop live transcoder for stream {StreamId}", streamId);
            }

            _logger.LogInformation("Live transcoder stopped for stream {StreamId}", streamId);
        }

        public bool IsRunning(int streamId)
        {
            return _processes.ContainsKey(streamId);
        }
    }
}
