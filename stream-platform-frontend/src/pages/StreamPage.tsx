import { useEffect, useRef } from 'react';
import { useParams } from 'react-router-dom';
import Hls from 'hls.js';
import { fetchStream } from '../services/api';
import { Stream } from '../types';

const StreamPage = () => {
  const { id } = useParams<{ id: string }>();
  const videoRef = useRef<HTMLVideoElement>(null);

  useEffect(() => {
    if (!id) return;

    fetchStream(id).then((stream) => {
      if (Hls.isSupported() && videoRef.current) {
        const hls = new Hls();
        hls.loadSource(stream.hlsUrl);
        hls.attachMedia(videoRef.current);
      } else if (videoRef.current?.canPlayType('application/vnd.apple.mpegurl')) {
        videoRef.current.src = stream.hlsUrl;
      }
    });
  }, [id]);

  return (
    <div style={{ padding: '20px' }}>
      <video
        ref={videoRef}
        controls
        autoPlay
        style={{ width: '100%', maxHeight: '70vh' }}
      />
    </div>
  );
};

export default StreamPage;