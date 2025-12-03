using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UMH
{
    public class UMH_Serial : IDisposable
    {
        public event Action<byte[]> OnFrameReceived;

        private SerialPort _serialPort;
        private bool _isRunning;
        private Thread _readThread;
        private readonly List<byte> _buffer = new List<byte>();
        private readonly object _bufferLock = new object();

        private const byte Header1 = 0xAA;
        private const byte Header2 = 0x55;
        private const byte Tail1 = 0x0D;
        private const byte Tail2 = 0x0A;

        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;
        public string PortName { get; private set; }
        public int BaudRate { get; private set; }

        public enum CommandType : byte
        {
            SetPoint = 0x01,
            EnableDisable = 0x02,
            GetStatus = 0x03,
            Ping = 0x04
        }

        public enum ResponseType : byte
        {
            ACK = 0x80,
            NACK = 0x81,
            ReturnStatus = 0x82,
            Ping_ACK = 0x83,
            Error = 0xFF
        }

        public bool Connect(string portName, int baudRate)
        {
            Disconnect();

            PortName = portName;
            BaudRate = baudRate;

            try
            {
                _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 100,
                    WriteTimeout = 500
                };
                _serialPort.Open();

                _isRunning = true;
                _readThread = new Thread(ReadLoop) { IsBackground = true };
                _readThread.Start();

                return true;
            }
            catch
            {
                Disconnect();
                return false;
            }
        }

        public void Disconnect()
        {
            _isRunning = false;
            if (_readThread != null && _readThread.IsAlive)
            {
                _readThread.Join(200);
            }

            if (_serialPort != null)
            {
                if (_serialPort.IsOpen) _serialPort.Close();
                _serialPort.Dispose();
                _serialPort = null;
            }

            lock (_bufferLock)
            {
                _buffer.Clear();
            }
        }

        public async Task<bool> SendFrameAsync(CommandType cmd, byte[] data = null)
        {
            if (!IsConnected) return false;

            byte dataLen = (byte)(data?.Length ?? 0);
            byte checksum = CalculateChecksum((byte)cmd, dataLen, data);

            int frameLen = 7 + dataLen;
            byte[] frame = new byte[frameLen];
            int idx = 0;

            frame[idx++] = Header1;
            frame[idx++] = Header2;
            frame[idx++] = (byte)cmd;
            frame[idx++] = dataLen;
            
            if (data != null && dataLen > 0)
            {
                Array.Copy(data, 0, frame, idx, dataLen);
                idx += dataLen;
            }

            frame[idx++] = checksum;
            frame[idx++] = Tail1;
            frame[idx++] = Tail2;

            try
            {
                await _serialPort.BaseStream.WriteAsync(frame, 0, frame.Length);
                await _serialPort.BaseStream.FlushAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void ReadLoop()
        {
            byte[] buffer = new byte[1024];
            while (_isRunning && _serialPort != null && _serialPort.IsOpen)
            {
                try
                {
                    int bytesRead = _serialPort.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        lock (_bufferLock)
                        {
                            for (int i = 0; i < bytesRead; i++)
                            {
                                _buffer.Add(buffer[i]);
                            }
                        }
                        ProcessBuffer();
                    }
                }
                catch (TimeoutException) { }
                catch
                {
                    if (_isRunning) Disconnect(); 
                }
            }
        }

        private void ProcessBuffer()
        {
            lock (_bufferLock)
            {
                while (_buffer.Count >= 7)
                {
                    if (_buffer[0] != Header1 || _buffer[1] != Header2)
                    {
                        _buffer.RemoveAt(0);
                        continue;
                    }

                    byte dataLen = _buffer[3];
                    int totalLen = 7 + dataLen;

                    if (_buffer.Count < totalLen) break;

                    if (_buffer[totalLen - 2] != Tail1 || _buffer[totalLen - 1] != Tail2)
                    {
                        _buffer.RemoveAt(0);
                        continue;
                    }

                    byte cmd = _buffer[2];
                    byte checksum = _buffer[totalLen - 3];
                    
                    byte[] payload = null;
                    if (dataLen > 0)
                    {
                        payload = new byte[dataLen];
                        _buffer.CopyTo(4, payload, 0, dataLen);
                    }

                    if (CalculateChecksum(cmd, dataLen, payload) == checksum)
                    {
                        byte[] frame = new byte[totalLen];
                        _buffer.CopyTo(0, frame, 0, totalLen);
                        OnFrameReceived?.Invoke(frame);
                        _buffer.RemoveRange(0, totalLen);
                    }
                    else
                    {
                        _buffer.RemoveAt(0);
                    }
                }
            }
        }

        private byte CalculateChecksum(byte cmd, byte len, byte[] data)
        {
            int sum = cmd + len;
            if (data != null)
            {
                foreach (byte b in data) sum += b;
            }
            return (byte)(sum & 0xFF);
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
