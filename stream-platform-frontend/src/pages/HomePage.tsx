import { useEffect, useState } from 'react';
import Grid from '@mui/material/Grid';
import Typography from '@mui/material/Typography';
import StreamCard from '../components/StreamCard';
import { fetchStreams } from '../services/api';
import { Stream } from '../types';

const HomePage = () => {
  const [streams, setStreams] = useState<Stream[]>([]);

  useEffect(() => {
    fetchStreams().then(setStreams);
  }, []);

  return (
    <div style={{ padding: '20px' }}>
      <Typography variant="h4" gutterBottom>Стримы</Typography>
      <Grid container spacing={3} component="div">
        {streams.map((stream) => (
          <Grid 
            key={stream.id}
            item
            xs={12}
            sm={6}
            md={4}
            component="div"
          >
            <StreamCard stream={stream} />
          </Grid>
        ))}
      </Grid>
    </div>
  );
};

export default HomePage;