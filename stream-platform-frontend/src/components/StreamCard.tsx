import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import CardMedia from '@mui/material/CardMedia';
import Typography from '@mui/material/Typography';
import { Link } from 'react-router-dom';
import { Stream } from '../types';

const StreamCard = ({ stream }: { stream: Stream }) => {
  return (
    <Link to={`/stream/${stream.id}`} style={{ textDecoration: 'none' }}>
      <Card>
        <CardMedia
          component="video"
          src={stream.hlsUrl}
          muted
          loop
          sx={{ height: 140 }}
        />
        <CardContent>
          <Typography variant="h6">{stream.title}</Typography>
          <Typography color={stream.isLive ? 'green' : 'red'}>
            {stream.isLive ? 'LIVE' : 'OFFLINE'}
          </Typography>
        </CardContent>
      </Card>
    </Link>
  );
};

export default StreamCard;