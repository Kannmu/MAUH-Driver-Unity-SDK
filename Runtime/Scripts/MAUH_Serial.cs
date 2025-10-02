using System.IO.Ports;
using System;
using UnityEngine;
// using RJCP.IO.Ports;

namespace MAUH
{
    public class MAUH_Serial : IDisposable
    {
        private bool isConnected = false;
        public bool IsConnected
        {
            get { return isConnected; }
            private set { isConnected = value; }
        }

        private string portName = "COM3";
        public string PortName
        {
            get { return portName; }
            set { portName = value; }
        }

        private readonly int[] validBaudRates = new int[] { 9600, 19200, 38400, 57600, 115200 };

        private int baudRate = 115200;
        public int BaudRate
        {
            get { return baudRate; }
            set
            {
                if (Array.IndexOf(validBaudRates, value) == -1)
                {
                    Debug.LogError("BaudRate must be one of 9600, 19200, 38400, 57600, 115200");
                    return;
                }
                baudRate = value;
            }
        }

        public SerialPort serialPort;

        private readonly byte[] frameHeader = new byte[] { 0xAA, 0x55 };
        private readonly byte[] frameTail = new byte[] { 0x0D, 0x0A };

        /// <summary>
        /// PC to MAUH Command Types from documentation
        /// </summary>
        public enum CommandType : byte
        {
            PointInfo = 0x01,
            EnableDisable = 0x02,
            GetStatus = 0x03,
            Ping = 0x04
        }

        /// <summary>
        /// MAUH to PC Response Types from documentation
        /// </summary>
        public enum ResponseType : byte
        {
            ACK = 0x80,
            NACK = 0x81,
            ReturnStatus = 0x82,
            Ping_ACK = 0x83,
            Error = 0xFF
        }


        /// <summary>
        /// Connect to the MAUH device with optional timeout
        /// </summary>
        /// <param name="timeout">Connection timeout in milliseconds (default: 200)</param>
        /// <returns>True if connection successful, false otherwise</returns>
        public bool Connect(int timeout = 200)
        {
            try
            {
                serialPort = new SerialPort(PortName, BaudRate);

                // Use Task.Run with timeout to control serialPort.Open() operation
                var openTask = System.Threading.Tasks.Task.Run(() =>
                    {
                        serialPort.Open();
                        serialPort.Parity = Parity.None;
                        serialPort.DataBits = 8;
                        serialPort.StopBits = StopBits.One;
                    }
                );
                bool completed = openTask.Wait(timeout);

                if (!completed)
                {
                    Debug.LogError($"Serial port connection timeout after {timeout}ms");
                    serialPort?.Dispose();
                    serialPort = null;
                    IsConnected = false;
                    return false;
                }

                // Check if task completed successfully
                if (openTask.IsFaulted)
                {
                    throw openTask.Exception?.GetBaseException() ?? new Exception("Unknown error during port opening");
                }

                IsConnected = serialPort.IsOpen;
                return IsConnected;
            }
            catch (Exception ex)
            {
                // Handle connection errors
                Debug.LogError($"Error opening serial port: {ex.Message}");
                serialPort?.Dispose();
                serialPort = null;
                IsConnected = false;
                return false;
            }
        }

        /// <summary>
        /// Find the port name of the MAUH device using polling all available ports and send Ping command to each port. 
        /// If the device responds with expect response, it is considered to be the MAUH device.
        /// </summary>
        /// <returns></returns>
        public string AttemptToConnectToPort(string portName = null)
        {
            if (portName != null)
            {
                PortName = portName;
            }
            try
            {
                if (Connect(100))
                {
                    // Send a ping command with a random payload
                    byte[] randomPayload = new byte[] { (byte)UnityEngine.Random.Range(0, 256) };
                    SendFrame(CommandType.Ping, randomPayload, 100);

                    // Wait for a response
                    byte[] response = ReadFrame(100);
                    if (response != null && response.Length > 4) // Header(2) + Type(1) + Length(1) + Checksum(1) + Tail(2) = 7, but payload can be 0
                    {
                        if (response[2] == (byte)ResponseType.Ping_ACK && response[4] == randomPayload[0])
                        {
                            Debug.Log($"MAUH device found on port: {portName}");
                            return portName;
                        }
                        else
                        {
                            Debug.LogWarning($"Port {portName} responded with unexpected data: {BitConverter.ToString(response)}");
                            PortName = null;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"Port {portName} responded with empty data");
                        PortName = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not ping port {portName}: {ex.Message}");
                PortName = null;
            }
            finally
            {
                Dispose();
            }
            return null;
        }

        /// <summary>
        /// 非阻塞式检查是否有可用数据
        /// </summary>
        /// <returns>如果有数据可读返回true</returns>
        public bool HasDataAvailable()
        {
            if (!IsConnected)
                return false;

            try
            {
                return serialPort.BytesToRead > 0;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error checking data availability: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 轮询专用的ReadFrame方法，使用很短的超时时间
        /// </summary>
        /// <param name="timeout">超时时间，默认10ms用于轮询</param>
        /// <returns>接收到的帧数据，如果没有数据则返回null</returns>
        public byte[] TryReadFrame(int timeout = 10)
        {
            if (!IsConnected || !HasDataAvailable())
            {
                return null;
            }

            return ReadFrame(timeout);
        }

        /// <summary>
        /// Calculate 8-bit sum checksum for the given command type, data length and data payload
        /// </summary>
        /// <param name="cmdType">Command or message type</param>
        /// <param name="dataLength">Length of data payload</param>
        /// <param name="data">Data payload (can be null)</param>
        /// <returns>8-bit checksum</returns>
        private byte CalculateChecksum(byte cmdType, byte dataLength, byte[] data = null)
        {
            byte checksum = cmdType;
            checksum += dataLength;
            if (data != null)
            {
                foreach (byte b in data)
                {
                    checksum += b;
                }
            }
            return checksum;
        }

        /// <summary>
        /// Send a frame to the MAUH device with the given command type and data payload.
        /// </summary>
        /// <param name="cmd">Command type to send</param>
        /// <param name="data">Optional data payload</param>
        /// <param name="timeout">Timeout in milliseconds (default: 200)</param>
        public void SendFrame(CommandType cmd, byte[] data = null, int timeout = 300)
        {
            if (!IsConnected)
            {
                Debug.LogError("Serial port is not connected.");
                return;
            }

            // Data length
            byte dataLength = (byte)(data?.Length ?? 0);

            // Calculate checksum using the common function
            byte checksum = CalculateChecksum((byte)cmd, dataLength, data);

            // Construct the frame
            byte[] frame = new byte[frameHeader.Length + 1 + 1 + dataLength + 1 + frameTail.Length];
            int index = 0;

            // Header
            Buffer.BlockCopy(frameHeader, 0, frame, index, frameHeader.Length);
            index += frameHeader.Length;

            // Command
            frame[index++] = (byte)cmd;

            // Data Length
            frame[index++] = dataLength;

            // Data
            if (data != null)
            {
                Buffer.BlockCopy(data, 0, frame, index, data.Length);
                index += data.Length;
            }

            // Checksum
            frame[index++] = checksum;

            // Tail
            Buffer.BlockCopy(frameTail, 0, frame, index, frameTail.Length);

            // Send the frame
            try
            {
                serialPort.WriteTimeout = timeout;
                serialPort.Write(frame, 0, frame.Length);
            }
            catch (TimeoutException)
            {
                Debug.LogWarning($"Write timeout after {timeout}ms");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error writing to serial port: {ex.Message}");
            }
        }

        /// <summary>
        /// Read a frame from the MAUH device.
        /// </summary>
        /// <param name="timeout">Timeout in milliseconds (default: 300)</param>
        /// <returns>Byte array representing the frame, or null if timeout or error occurs</returns>
        public byte[] ReadFrame(int timeout = 300)
        {
            if (!IsConnected)
            {
                Debug.LogError("Serial port is not connected.");
                return null;
            }

            try
            {
                serialPort.ReadTimeout = timeout;
                // Wait for header
                int byte1 = serialPort.ReadByte();
                int byte2 = serialPort.ReadByte();

                if (byte1 == frameHeader[0] && byte2 == frameHeader[1])
                {
                    // Header found, read the rest of the frame
                    byte msgType = (byte)serialPort.ReadByte();
                    byte dataLength = (byte)serialPort.ReadByte();
                    byte[] data = new byte[dataLength];
                    if (dataLength > 0)
                    {
                        serialPort.Read(data, 0, dataLength);
                    }
                    byte checksum = (byte)serialPort.ReadByte();

                    // Verify checksum using the common function
                    byte calculatedChecksum = CalculateChecksum(msgType, dataLength, data);

                    if (checksum == calculatedChecksum)
                    {
                        // Wait for tail
                        int tail1 = serialPort.ReadByte();
                        int tail2 = serialPort.ReadByte();

                        if (tail1 == frameTail[0] && tail2 == frameTail[1])
                        {
                            // Frame is valid
                            byte[] fullFrame = new byte[2 + 1 + 1 + dataLength + 1 + 2];
                            int index = 0;
                            Buffer.BlockCopy(frameHeader, 0, fullFrame, index, frameHeader.Length);
                            index += frameHeader.Length;
                            fullFrame[index++] = msgType;
                            fullFrame[index++] = dataLength;
                            if (dataLength > 0)
                            {
                                Buffer.BlockCopy(data, 0, fullFrame, index, data.Length);
                                index += data.Length;
                            }
                            fullFrame[index++] = checksum;
                            Buffer.BlockCopy(frameTail, 0, fullFrame, index, frameTail.Length);
                            return fullFrame;
                        }
                    }
                }
            }
            catch (TimeoutException)
            {
                // Normal timeout, no data received
                Debug.LogWarning("Read timeout reached. No data received.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error reading from serial port: {ex.Message}");
            }

            return null;
        }


        /// <summary>
        /// Dispose of the serial port resources.
        /// </summary>
        public void Dispose()
        {
            if (serialPort != null && serialPort.IsOpen)
            {
                serialPort.Close();
            }
        }
    }



}







