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
            {
                _logger.LogWarning("Live transcoder already running for stream {StreamId}", stream.Id);
                return;
            }

            var streamKey = user.StreamKey;
            var baseOutputDir = Path.Combine(LiveBasePath, stream.PublicId);

            Directory.CreateDirectory(baseOutputDir);
            Directory.CreateDirectory(Path.Combine(baseOutputDir, "1080p"));
            Directory.CreateDirectory(Path.Combine(baseOutputDir, "720p"));
            Directory.CreateDirectory(Path.Combine(baseOutputDir, "480p"));

            var masterPlaylist = Path.Combine(baseOutputDir, "master.m3u8");

            var args =
                "-hide_banner -loglevel warning " +
                $"-i {RtmpBaseUrl}/{streamKey} " +

                // ====== video split ======
                "-filter_complex " +
                "\"[0:v]split=3[v1080][v720][v480];" +
                "[v1080]scale=1920:1080[v1080out];" +
                "[v720]scale=1280:720[v720out];" +
                "[v480]scale=854:480[v480out]\" " +

                // ====== mapping ======
                "-map [v1080out] -map 0:a " +
                "-map [v720out]  -map 0:a " +
                "-map [v480out]  -map 0:a " +

                // ====== encoding ======
                "-c:v libx264 -preset veryfast -tune zerolatency " +
                "-c:a aac -b:a 128k " +

                "-b:v:0 6000k -maxrate:v:0 6500k -bufsize:v:0 12000k " +
                "-b:v:1 3000k -maxrate:v:1 3500k -bufsize:v:1 6000k " +
                "-b:v:2 1200k -maxrate:v:2 1500k -bufsize:v:2 3000k " +

                // ====== HLS ======
                "-f hls " +
                "-hls_time 2 " +
                "-hls_list_size 6 " +
                "-hls_flags delete_segments " +
                "-hls_segment_filename \"" + baseOutputDir + "/%v/segment_%03d.ts\" " +
                "-master_pl_name master.m3u8 " +
                "-var_stream_map \"v:0,a:0 v:1,a:1 v:2,a:2\" " +
                $"\"{baseOutputDir}/%v/index.m3u8\"";

            var psi = new ProcessStartInfo
            {
                FileName = FfmpegPath,
                Arguments = args,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    _logger.LogError("[FFmpeg][Stream {StreamId}] {Line}", stream.Id, e.Data);
            };

            process.Exited += (_, _) =>
            {
                _processes.TryRemove(stream.Id, out _);
                _logger.LogInformation("Live transcoder exited for stream {StreamId}", stream.Id);
            };

            if (!process.Start())
                throw new InvalidOperationException("Failed to start ffmpeg process");

            process.BeginErrorReadLine();
            _processes[stream.Id] = process;

            _logger.LogInformation(
                "Live transcoder started. StreamId={StreamId}, PublicId={PublicId}",
                stream.Id,
                stream.PublicId
            );

            await Task.CompletedTask;
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
