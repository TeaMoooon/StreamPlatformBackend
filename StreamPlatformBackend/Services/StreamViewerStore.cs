using System.Collections.Concurrent;

namespace StreamPlatformBackend.Services
{
    public static class StreamViewerStore
    {
        private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, byte>> ViewersByStreamer = new();

        public static int RegisterViewer(int streamerId, string viewerKey)
        {
            var viewers = ViewersByStreamer.GetOrAdd(streamerId, _ => new ConcurrentDictionary<string, byte>());
            viewers[viewerKey] = 0;
            return viewers.Count;
        }

        public static int UnregisterViewer(int streamerId, string viewerKey)
        {
            if (!ViewersByStreamer.TryGetValue(streamerId, out var viewers))
                return 0;

            viewers.TryRemove(viewerKey, out _);
            return viewers.Count;
        }

        public static int GetViewerCount(int streamerId)
        {
            return ViewersByStreamer.TryGetValue(streamerId, out var viewers) ? viewers.Count : 0;
        }
    }
}
