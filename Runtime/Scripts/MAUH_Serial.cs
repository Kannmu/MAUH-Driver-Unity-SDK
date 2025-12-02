using System.IO.Ports;
using System;
using UnityEngine;
using System.Threading.Tasks;

namespace MAUH
{
    public class MAUH_Serial : IDisposable
    {
        public bool IsConnected
        {
            get { return serialPort != null && serialPort.IsOpen; }
        }

        private string portName = "COM4";
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

        // 事件：当接收到完整帧时触发
        public event Action<byte[]> OnFrameReceived;

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
                serialPort = new SerialPort(PortName, BaudRate, Parity.None, 8, StopBits.One);
                
                // 使用Task.Run控制连接超时
                var openTask = Task.Run(() => serialPort.Open());
                bool completed = openTask.Wait(timeout);

                if (!completed)
                {
                    Debug.LogError($"Serial port connection timeout after {timeout}ms");
                    serialPort?.Dispose();
                    serialPort = null;
                    return false;
                }

                // 检查任务是否成功完成
                if (openTask.IsFaulted)
                {
                    throw openTask.Exception?.GetBaseException() ?? new Exception("Unknown error during port opening");
                }

                if (serialPort.IsOpen)
                {
                    _ = StartContinuousReadingAsync();
                }

                return serialPort.IsOpen;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error opening serial port: {ex.Message}");
                serialPort?.Dispose();
                serialPort = null;
                return false;
            }
        }

        /// <summary>
        /// 启动持续的异步读取任务
        /// </summary>
        private async Task StartContinuousReadingAsync()
        {
            try
            {
                while (IsConnected && serialPort != null && serialPort.IsOpen)
                {
                    byte[] frame = await ReadFrameAsync();

                    if (frame != null)
                    {
                        OnFrameReceived?.Invoke(frame);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error in continuous reading: {ex.Message}");
            }
        }

        /// <summary>
        /// Find the port name of the MAUH device using polling all available ports and send Ping command to each port. 
        /// If the device responds with expect response, it is considered to be the MAUH device.
        /// </summary>
        /// <returns></returns>
        public async Task<string> AttemptToConnectToPortAsync(string portName = null)
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

                    Debug.Log($"Sending Ping command with payload: {BitConverter.ToString(randomPayload)}");

                    var tcs = new TaskCompletionSource<byte[]>();
                    void Handler(byte[] frame) { tcs.TrySetResult(frame); }
                    OnFrameReceived += Handler;

                    if (!await SendFrameAsync(CommandType.Ping, randomPayload, 100))
                    {
                        Debug.LogError($"Failed to send Ping command to port {portName}");
                        Dispose();
                        return null;
                    }

                    // 等待响应（最多500ms）
                    var completed = await Task.WhenAny(tcs.Task, Task.Delay(500));
                    OnFrameReceived -= Handler;
                    byte[] response = completed == tcs.Task ? tcs.Task.Result : null;

                    Debug.Log($"Received response: {BitConverter.ToString(response)}");

                    if (response != null && response.Length > 4)
                    {
                        if (response[2] == (byte)ResponseType.Ping_ACK && response[4] == randomPayload[0])
                        {
                            Debug.Log($"MAUH device found on port: {portName}");
                            return portName;
                        }
                        else
                        {
                            Debug.LogWarning($"Port {portName} responded with unexpected data: {BitConverter.ToString(response)}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"Port {portName} responded with empty data");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not ping port {portName}: {ex.Message}");
            }
            finally
            {
                Dispose();
            }
            return null;
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
        /// Send a frame to the MAUH device with the given command type and data payload using async method.
        /// </summary>
        /// <param name="cmd">Command type to send</param>
        /// <param name="data">Optional data payload</param>
        /// <param name="timeout">Timeout in milliseconds (default: 300)</param>
        public async Task<bool> SendFrameAsync(CommandType cmd, byte[] data = null, int timeout = 300)
        {
            if (serialPort == null || !serialPort.IsOpen)
            {
                Debug.LogError($"Serial port {PortName} is not connected.");
                return false;
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

            // Send the frame using async method
            try
            {
                Debug.Log($"Attempting to write {frame.Length} bytes to {PortName}. Port Open: {serialPort.IsOpen}. Bytes to Write Queue: {serialPort.BytesToWrite}.");
                Debug.Log($"Frame data: {BitConverter.ToString(frame)}");

                serialPort.WriteTimeout = timeout;
                await serialPort.BaseStream.WriteAsync(frame, 0, frame.Length);
                // 使用Flush确保数据完全发送
                await serialPort.BaseStream.FlushAsync();
                Debug.Log("WriteAsync and FlushAsync completed successfully.");
                return true;
            }
            catch (TimeoutException)
            {
                Debug.LogWarning($"Write timeout after {timeout}ms on port {PortName}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error writing to serial port {PortName}: {ex}");
                // Debug.LogError($"Error writing to serial port {PortName}: {ex.Message}");
                if (serialPort != null)
                {
                    Debug.LogError($"Serial Port state on error: IsOpen={serialPort.IsOpen}, BytesToWrite={serialPort.BytesToWrite}, BytesToRead={serialPort.BytesToRead}");
                }
                else
                {
                    Debug.LogError("Serial Port object is null on error.");
                }
                return false;
            }
        }

        /// <summary>
        /// Read a frame from the MAUH device using async method.
        /// </summary>
        /// <param name="timeout">Timeout in milliseconds (default: 1000)</param>
        /// <returns>Byte array representing the frame, or null if timeout or error occurs</returns>
        public async Task<byte[]> ReadFrameAsync(int timeout = 1000)
        {
            if (!IsConnected)
            {
                Debug.LogError("Serial port is not connected.");
                return null;
            }

            try
            {
                serialPort.ReadTimeout = timeout;
    
                // 使用异步方法读取帧头
                byte[] headerBuffer = new byte[2];
                int headerRead = 0;
                while (headerRead < 2)
                {
                    int bytesRead = await Task.Run(() => serialPort.Read(headerBuffer, headerRead, 2 - headerRead));
                    if (bytesRead == 0) return null; // 没有更多数据
                    headerRead += bytesRead;
                }

                // 检查帧头
                if (headerBuffer[0] == frameHeader[0] && headerBuffer[1] == frameHeader[1])
                {
                    // 读取消息类型和数据长度
                    byte[] typeAndLength = new byte[2];
                    int typeRead = 0;
                    while (typeRead < 2)
                    {
                        int bytesRead = await Task.Run(() => serialPort.Read(typeAndLength, typeRead, 2 - typeRead));
                        if (bytesRead == 0) return null;
                        typeRead += bytesRead;
                    }

                    byte msgType = typeAndLength[0];
                    byte dataLength = typeAndLength[1];

                    // 读取数据负载
                    byte[] data = new byte[dataLength];
                    if (dataLength > 0)
                    {
                        int dataRead = 0;
                        while (dataRead < dataLength)
                        {
                            int bytesRead = await Task.Run(() => serialPort.Read(data, dataRead, dataLength - dataRead));
                            if (bytesRead == 0) return null;
                            dataRead += bytesRead;
                        }
                    }

                    // 读取校验和
                    byte[] checksumBuffer = new byte[1];
                    await Task.Run(() => serialPort.Read(checksumBuffer, 0, 1));
                    byte checksum = checksumBuffer[0];

                    // 验证校验和
                    byte calculatedChecksum = CalculateChecksum(msgType, dataLength, data);
                    if (checksum == calculatedChecksum)
                    {
                        // 读取帧尾
                        byte[] tailBuffer = new byte[2];
                        int tailRead = 0;
                        while (tailRead < 2)
                        {
                            int bytesRead = await Task.Run(() => serialPort.Read(tailBuffer, tailRead, 2 - tailRead));
                            if (bytesRead == 0) return null;
                            tailRead += bytesRead;
                        }

                        if (tailBuffer[0] == frameTail[0] && tailBuffer[1] == frameTail[1])
                        {
                            // 构建完整帧
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
                // 正常超时，没有接收到数据
                Debug.LogWarning($"Read timeout reached during frame read on port {PortName}. No data received.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error reading from serial port {PortName}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 同步版本的SendFrame，为了向后兼容
        /// </summary>
        public bool SendFrame(CommandType cmd, byte[] data = null, int timeout = 300)
        {
            return SendFrameAsync(cmd, data, timeout).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Dispose of the serial port resources.
        /// </summary>
        public void Dispose()
        {
            
            if (serialPort != null && serialPort.IsOpen)
            {
                serialPort.Close();
                PortName = null;
            }
            
            serialPort?.Dispose();
            serialPort = null;
        }
    }
}

