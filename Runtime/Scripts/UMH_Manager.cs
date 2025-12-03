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
        public event Action<byte> OnErrorReceived;

        private UMH_Serial _serial;
        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
        private bool _isScanning;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (Instance == null)
            {
                var obj = new GameObject("[UMH_Manager]");
                // obj.hideFlags = HideFlags.HideInHierarchy;
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

        public async Task<bool> SendCommandAsync(UMH_Serial.CommandType cmd, byte[] data = null)
        {
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
                    Debug.Log($"[UMH] Command Acknowledged (ACK) at {DateTime.Now:HH:mm:ss.fff}");
                    break;
                case UMH_Serial.ResponseType.NACK:
                    Debug.LogWarning($"[UMH] Command Not Acknowledged (NACK) at {DateTime.Now:HH:mm:ss.fff}");
                    break;
                case UMH_Serial.ResponseType.ReturnStatus:
                    if (payload != null && payload.Length > 0)
                    {
                        UMH_Device_Status newStatus = new UMH_Device_Status();
                        int offset = 0;
                        newStatus.Voltage = BitConverter.ToSingle(payload[offset..(offset += 4)]);
                        newStatus.Temperature = BitConverter.ToSingle(payload[offset..(offset += 4)]);
                        OnStatusReceived?.Invoke(newStatus);
                        Debug.Log($"<color=lightgray>[UMH] Status Received: Voltage={newStatus.Voltage:F2}V, Temperature={newStatus.Temperature:F1}°C at {DateTime.Now:HH:mm:ss.fff}</color>");
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
                        Debug.LogError($"[UMH] Error Received: Code {payload[0]:X2}");
                    }
                    break;
            }
        }

        #region Protocol Commands

        /// <summary>
        /// Command 0x01: Point Info
        /// </summary>
        public async Task SetPointAsync(byte[] data)
        {
            await SendCommandAsync(UMH_Serial.CommandType.SetPoint, data);
        }

        /// <summary>
        /// Command 0x02: Enable/Disable
        /// </summary>
        /// <param name="enable">true to enable, false to disable</param>
        public async Task SetEnableAsync(bool enable)
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
            yield return new WaitForSeconds(1f);
            while (UMH_API.IsConnected)
            {
                UMH_API.GetStatus();
                yield return new WaitForSeconds(1f);
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
