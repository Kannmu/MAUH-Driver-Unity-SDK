using System;
using System.Collections.Concurrent;
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
            _serial.OnFrameReceived += HandleFrameReceived;
        }

        private void Start()
        {
            _ = ScanAndConnectAsync();
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
            switch (type)
            {
                case UMH_Serial.ResponseType.Ping_ACK:
                    Debug.Log($"Ping ACK received: {frame[4]}");
                    break;
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
            if (!serial.Connect(port, 115200)) return false;

            byte pingVal = (byte)UnityEngine.Random.Range(0, 255);
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

            var task = await Task.WhenAny(tcs.Task, Task.Delay(100));
            serial.OnFrameReceived -= handler;

            return task == tcs.Task && tcs.Task.Result;
        }
    }
}
