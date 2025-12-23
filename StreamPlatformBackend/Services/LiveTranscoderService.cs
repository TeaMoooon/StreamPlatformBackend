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

            var args = $@"
-hide_banner -loglevel info

-i {RtmpBaseUrl}/{streamKey}

-filter_complex ""
[0:v]split=3[v1080][v720][v480];
[0:a]asplit=3[a1080][a720][a480];
[v1080]scale=1920:1080,fps=30[v1080out];
[v720]scale=1280:720,fps=30[v720out];
[v480]scale=854:480,fps=30[v480out]
""

-map ""[v1080out]"" -map ""[a1080]""
-map ""[v720out]""  -map ""[a720]""
-map ""[v480out]""  -map ""[a480]""

-c:v libx264 -preset veryfast -profile:v main -level 4.1
-g 60 -keyint_min 60 -sc_threshold 0

-c:a aac

-b:v:0 6000k -maxrate:v:0 6500k -bufsize:v:0 12000k
-b:v:1 3000k -maxrate:v:1 3500k -bufsize:v:1 6000k
-b:v:2 1500k -maxrate:v:2 1800k -bufsize:v:2 3000k

-b:a:0 160k
-b:a:1 128k
-b:a:2 96k

-f hls
-hls_time 3
-hls_list_size 12
-hls_flags delete_segments+append_list
-master_pl_name ""master.m3u8""
-hls_segment_filename ""{baseDir}/%v/index_%03d.ts""
-var_stream_map ""v:0,a:0 v:1,a:1 v:2,a:2""
""{baseDir}/%v/index.m3u8""
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
                    _logger.LogInformation("[ffmpeg:{StreamId}] {Line}", stream.Id, e.Data);
            };
            process.BeginErrorReadLine();

            _processes[stream.Id] = process;

            _logger.LogInformation("Live transcoder started for stream {StreamId}", stream.Id);
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
