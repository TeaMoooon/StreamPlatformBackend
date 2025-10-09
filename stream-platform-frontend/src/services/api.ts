import axios from 'axios';
import { Stream } from '../types';

const API_URL = 'http://localhost:5000/api';

export const fetchStreams = async (): Promise<Stream[]> => {
  const response = await axios.get(`${API_URL}/stream`);
  return response.data;
};

export const fetchStream = async (id: string): Promise<Stream> => {
  const response = await axios.get(`${API_URL}/stream/${id}`);
  return response.data;
};