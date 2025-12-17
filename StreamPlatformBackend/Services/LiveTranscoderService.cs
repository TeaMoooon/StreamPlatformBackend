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
            var outputDir = Path.Combine(LiveBasePath, streamKey, "720p");
            Directory.CreateDirectory(outputDir);

            var outputPlaylist = Path.Combine(outputDir, "index.m3u8");

            var args = $"-hide_banner -loglevel warning " +
                       $"-i {RtmpBaseUrl}/{streamKey} " +
                       "-c:v libx264 -preset veryfast -tune zerolatency " +
                       "-s 1280x720 -b:v 3000k " +
                       "-c:a aac -b:a 128k " +
                       "-f hls -hls_time 2 -hls_list_size 6 -hls_flags delete_segments " +
                       $"\"{outputPlaylist}\"";

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

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _logger.LogInformation("[FFmpeg][Stream {StreamId}] {Line}", stream.Id, e.Data);
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _logger.LogError("[FFmpeg][Stream {StreamId}] {Line}", stream.Id, e.Data);
            };

            process.Exited += (_, _) =>
            {
                _processes.TryRemove(stream.Id, out _);
                _logger.LogInformation("Live transcoder exited for stream {StreamId}", stream.Id);
            };

            if (!process.Start())
                throw new InvalidOperationException("Failed to start ffmpeg process");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _processes[stream.Id] = process;

            _logger.LogInformation(
                "Live transcoder started. StreamId={StreamId}, StreamKey={StreamKey}, Output={OutputPlaylist}",
                stream.Id,
                streamKey,
                outputPlaylist
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
