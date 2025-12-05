using System;
using System.Collections.Concurrent;
using System.Collections;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading.Tasks;
using UnityEngine;

namespace UMH
{
    public class UMH_Manager : MonoBehaviour
    {
        public static UMH_Manager Instance { get; private set; }
        public bool IsConnected => _serial != null && _serial.IsConnected;
        
        public event Action<byte[]> OnDataReceived;
        public event Action<UMH_Device_Status> OnStatusReceived;
        public event Action<Vector3> OnPointSent;
        public event Action<double> OnPACKReceived;
        public event Action<byte> OnErrorReceived;
        public float RefreshRate = 30.0f;
        private UMH_Serial _serial;
        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
        private bool _isScanning;
        private const string _common_info_color = "#686868ff";
        private const string _error_color = "#FF4040";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (Instance == null)
            {
                var obj = new GameObject("[UMH_Manager]");
                obj.hideFlags = HideFlags.HideInHierarchy;
                Instance = obj.AddComponent<UMH_Manager>();
                DontDestroyOnLoad(obj);
            }
        }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _serial = new UMH_Serial();

            _serial.OnFrameReceived -= HandleFrameReceived;
            _serial.OnFrameReceived += HandleFrameReceived;

            OnStatusReceived -= UMH_API.HandleStatusUpdate;
            OnStatusReceived += UMH_API.HandleStatusUpdate;

            OnPointSent -= UMH_API.HandlePointSent;
            OnPointSent += UMH_API.HandlePointSent;

            OnPACKReceived -= UMH_API.HandlePACKReceived;
            OnPACKReceived += UMH_API.HandlePACKReceived;
        }

        private void Start()
        {
            _ = ScanAndConnectAsync();
            StartCoroutine(GetStatusCoroutine());
        }

        private void Update()
        {
            while (_mainThreadActions.TryDequeue(out var action))
            {
                action?.Invoke();
            }
        }

        private void OnDestroy()
        {
            if (_serial != null)
            {
                _serial.OnFrameReceived -= HandleFrameReceived;
                _serial.Dispose();
            }
        }

        public void Connect(string portName, int baudRate)
        {
            _serial.Connect(portName, baudRate);
        }

        public void Reconnect()
        {
            if (_serial != null)
            {
                _serial.Dispose();
            }

            _serial = new UMH_Serial();
            
            _serial.OnFrameReceived -= HandleFrameReceived;
            _serial.OnFrameReceived += HandleFrameReceived;
            _ = ScanAndConnectAsync();
        }

        public async Task<bool> SendCommandAsync(UMH_Serial.CommandType cmd, byte[] data = null)
        {
            if (!_serial.IsConnected) return false;
            return await _serial.SendFrameAsync(cmd, data);
        }

        private void HandleFrameReceived(byte[] frame)
        {
            _mainThreadActions.Enqueue(() => 
            {
                OnDataReceived?.Invoke(frame);
                ProcessFrame(frame);
            });
        }

        private void ProcessFrame(byte[] frame)
        {
            if (frame.Length < 4) return;
            
            UMH_Serial.ResponseType type = (UMH_Serial.ResponseType)frame[2];
            byte dataLen = frame[3];
            byte[] payload = null;
            
            if (dataLen > 0 && frame.Length >= 7 + dataLen)
            {
                payload = new byte[dataLen];
                Array.Copy(frame, 4, payload, 0, dataLen);
            }

            switch (type)
            {
                case UMH_Serial.ResponseType.ACK:
                    // Debug.Log($"<color={_common_info_color}>[UMH] Command Acknowledged (ACK) at {DateTime.Now:HH:mm:ss.fff}</color>");
                    break;
                case UMH_Serial.ResponseType.NACK:
                    Debug.LogWarning($"<color={_common_info_color}>[UMH] Command Not Acknowledged (NACK) at {DateTime.Now:HH:mm:ss.fff}</color>");
                    break;
                case UMH_Serial.ResponseType.PACK:
                    if (payload != null && payload.Length > 0)
                    {
                        double updateDeltaTime = BitConverter.ToDouble(payload[0..8]);
                        OnPACKReceived?.Invoke(updateDeltaTime);
                        // Debug.Log($"<color={_common_info_color}>[UMH] Point Sent: UpdateDeltaTime={updateDeltaTime}s at {DateTime.Now:HH:mm:ss.fff}</color>");
                    }
                    break;
                case UMH_Serial.ResponseType.ReturnStatus:
                    if (payload != null && payload.Length > 0)
                    {
                        UMH_Device_Status newStatus = new();
                        int offset = 0;
                        newStatus.Voltage = BitConverter.ToSingle(payload[offset..(offset += 4)]);
                        newStatus.Temperature = BitConverter.ToSingle(payload[offset..(offset += 4)]);
                        newStatus.LoopFreq = BitConverter.ToSingle(payload[offset..(offset += 4)]);
                        newStatus.CalibrationMode = BitConverter.ToInt32(payload[offset..(offset += 4)]);
                        newStatus.SimulationMode = BitConverter.ToInt32(payload[offset..(offset += 4)]);
                        OnStatusReceived?.Invoke(newStatus);
                        // Debug.Log($"<color={_common_info_color}>[UMH] Status Received: Voltage={newStatus.Voltage:F2}V, Temperature={newStatus.Temperature:F1}°C, LoopFreq={newStatus.LoopFreq}Hz at {DateTime.Now:HH:mm:ss.fff}</color>");
                    }
                    break;
                case UMH_Serial.ResponseType.Ping_ACK:
                    // Ping ACK is mainly used for connection verification in ScanAndConnectAsync
                    // But we can log it here if needed
                    break;
                case UMH_Serial.ResponseType.Error:
                    if (payload != null && payload.Length > 0)
                    {
                        OnErrorReceived?.Invoke(payload[0]);
                        Debug.LogError($"<color={_error_color}>[UMH] Error Received: Code {payload[0]:X2}</color>");
                    }
                    break;
            }
        }

        #region Protocol Commands

        /// <summary>
        /// Command 0x01: Point Info
        /// </summary>
        public async void SetPointAsync(UMH_Point point)
        {
            byte[] data = new byte[32];
            int offset = 0;
            Array.Copy(BitConverter.GetBytes(point.Position[0]), 0, data, offset, 4);
            offset += 4;
            Array.Copy(BitConverter.GetBytes(point.Position[1]), 0, data, offset, 4);
            offset += 4;
            Array.Copy(BitConverter.GetBytes(point.Position[2]), 0, data, offset, 4);
            offset += 4;
            Array.Copy(BitConverter.GetBytes(point.Strength), 0, data, offset, 4);
            offset += 4;
            Array.Copy(BitConverter.GetBytes(point.Vibration[0]), 0, data, offset, 4);
            offset += 4;
            Array.Copy(BitConverter.GetBytes(point.Vibration[1]), 0, data, offset, 4);
            offset += 4;
            Array.Copy(BitConverter.GetBytes(point.Vibration[2]), 0, data, offset, 4);
            offset += 4;
            Array.Copy(BitConverter.GetBytes(point.Frequency), 0, data, offset, 4);
            OnPointSent?.Invoke(point.Position);
            await SendCommandAsync(UMH_Serial.CommandType.SetPoint, data);
        }

        /// <summary>
        /// Command 0x02: Enable/Disable
        /// </summary>
        /// <param name="enable">true to enable, false to disable</param>
        public async void SetEnableAsync(bool enable)
        {
            byte val = enable ? (byte)0x01 : (byte)0x00;
            await SendCommandAsync(UMH_Serial.CommandType.EnableDisable, new byte[] { val });
        }

        /// <summary>
        /// Command 0x03: GetStatus
        /// </summary>
        public async void GetStatusAsync()
        {
            await SendCommandAsync(UMH_Serial.CommandType.GetStatus);
        }

        #endregion

        private IEnumerator GetStatusCoroutine()
        {
            while (true)
            {
                yield return new WaitUntil(() => UMH_API.IsConnected);
                while (UMH_API.IsConnected)
                {
                    UMH_API.SendGetStatusCommand();
                    yield return new WaitForSeconds(1.0f / RefreshRate);
                }
                yield return null;
            }
        }


        private async Task ScanAndConnectAsync()
        {
            if (_isScanning || IsConnected) return;
            _isScanning = true;

            string[] ports = SerialPort.GetPortNames();
            if (ports.Length == 0)
            {
                _isScanning = false;
                return;
            }

            var tcs = new TaskCompletionSource<UMH_Serial>();
            var tasks = new List<Task>();

            foreach (var port in ports)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var serial = new UMH_Serial();
                        if (await CheckPort(serial, port))
                        {
                            if (!tcs.TrySetResult(serial))
                            {
                                serial.Dispose();
                            }
                        }
                        else
                        {
                            serial.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error scanning port {port}: {ex.Message}");
                    }
                }));
            }

            var completedTask = await Task.WhenAny(tcs.Task, Task.WhenAll(tasks));

            if (completedTask == tcs.Task)
            {
                var newSerial = await tcs.Task;
                if (_serial != null)
                {
                    _serial.OnFrameReceived -= HandleFrameReceived;
                    _serial.Dispose();
                }
                _serial = newSerial;
                _serial.OnFrameReceived += HandleFrameReceived;
                Debug.Log($"UMH Device Connected on {_serial.PortName}");
            }

            _isScanning = false;
        }
        private async Task<bool> CheckPort(UMH_Serial serial, string port)
        {
            // Skip ports with "Bluetooth" in the name as requested
            if (port.IndexOf("Bluetooth", StringComparison.OrdinalIgnoreCase) >= 0) return false;

            if (!serial.Connect(port, 115200)) return false;

            byte pingVal = (byte)new System.Random().Next(0, 255);
            var tcs = new TaskCompletionSource<bool>();
            
            Action<byte[]> handler = (frame) =>
            {
                if (frame.Length > 4 && 
                    frame[2] == (byte)UMH_Serial.ResponseType.Ping_ACK && 
                    frame[4] == pingVal)
                {
                    tcs.TrySetResult(true);
                }
            };

            serial.OnFrameReceived += handler;
            await serial.SendFrameAsync(UMH_Serial.CommandType.Ping, new byte[] { pingVal });

            var task = await Task.WhenAny(tcs.Task, Task.Delay(200));
            serial.OnFrameReceived -= handler;

            return task == tcs.Task && tcs.Task.Result;
        }
    }
}
