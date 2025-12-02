using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading.Tasks;
using System.IO.Ports;

namespace MAUH
{
    internal class MAUH_Manager : MonoBehaviour
    {
        public bool IsConnected { get { return mauh_serial.IsConnected; } }
        public static MAUH_Manager Instance { get; private set; }

        [NonSerialized] public MAUH_Serial mauh_serial;

        [NonSerialized] public System.Action<byte[]> OnDataReceived; // 数据接收事件

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitializeOnLoad()
        {
            if (Instance != null)
            {
                return;
            }

            GameObject managerObject = new GameObject("[MAUH_SerialManager]");
            Instance = managerObject.AddComponent<MAUH_Manager>();
            managerObject.hideFlags = HideFlags.HideInHierarchy;
        }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject); // 确保管理器在切换场景时不会被销毁
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            mauh_serial = new MAUH_Serial();

            // 订阅帧接收事件
            mauh_serial.OnFrameReceived += OnFrameReceived;

            Debug.Log("MAUH Device Initialized Successfully.");
        }

        private async Task Start()
        {
            // 启动设备查找
            // _ = FindDevicePortNameAsync();

            Connect("COM4", 115200);
            await SendCommandAsync(MAUH_Serial.CommandType.Ping, new byte[] { (byte)UnityEngine.Random.Range(0, 256) }, 0);
        }

        private void Update()
        {

        }

        /// <summary>
        /// 当接收到完整帧时的处理方法
        /// </summary>
        /// <param name="frame">接收到的完整帧数据</param>
        private void OnFrameReceived(byte[] frame)
        {
            OnDataReceived?.Invoke(frame);
            ProcessReceivedData(frame);
        }

        /// <summary>
        /// 异步查找设备端口名称
        /// </summary>
        private async Task FindDevicePortNameAsync()
        {
            string[] portNames = SerialPort.GetPortNames();

            // Reverse portNames array for fast connection attempt(because the last connected port is usually the one we want)
            Array.Reverse(portNames);

            if (portNames == null)
            {
                Debug.LogError("Failed to get serial port names.");
                return;
            }

            Debug.Log($"Available ports num: {portNames.Length}, ports: {string.Join(", ", portNames)}");

            if (portNames.Length == 0)
            {
                Debug.LogError("No serial ports available.");
                return;
            }

            Connect("COM4", 115200);
        }

        private void ProcessReceivedData(byte[] frame)
        {
            // 根据协议解析数据
            if (frame.Length >= 4) // 最小帧长度检查
            {
                MAUH_Serial.ResponseType responseType = (MAUH_Serial.ResponseType)frame[2];

                switch (responseType)
                {
                    case MAUH_Serial.ResponseType.ACK:
                        Debug.Log("收到ACK响应");
                        break;
                    case MAUH_Serial.ResponseType.NACK:
                        Debug.LogWarning("收到NACK响应");
                        break;
                    case MAUH_Serial.ResponseType.ReturnStatus:
                        Debug.Log("收到状态返回");
                        break;
                    case MAUH_Serial.ResponseType.Ping_ACK:
                        Debug.Log("收到Ping ACK" + BitConverter.ToString(frame));
                        break;
                    case MAUH_Serial.ResponseType.Error:
                        Debug.LogError("收到错误响应");
                        break;
                }
            }
        }

        public void Connect(string portName, int baudRate)
        {
            mauh_serial.PortName = portName;
            mauh_serial.BaudRate = baudRate;
            mauh_serial.Connect();
        }

        /// <summary>
        /// 异步发送命令
        /// </summary>
        /// <param name="cmd">命令类型</param>
        /// <param name="data">数据负载</param>
        /// <param name="timeout">超时时间</param>
        /// <returns>发送是否成功</returns>
        public async Task<bool> SendCommandAsync(MAUH_Serial.CommandType cmd, byte[] data = null, int timeout = 300)
        {
            return await mauh_serial.SendFrameAsync(cmd, data, timeout);
        }

        /// <summary>
        /// 同步发送命令（为了向后兼容）
        /// </summary>
        /// <param name="cmd">命令类型</param>
        /// <param name="data">数据负载</param>
        /// <param name="timeout">超时时间</param>
        /// <returns>发送是否成功</returns>
        public bool SendCommand(MAUH_Serial.CommandType cmd, byte[] data = null, int timeout = 300)
        {
            return mauh_serial.SendFrame(cmd, data, timeout);
        }

        private void OnDestroy()
        {
            if (mauh_serial != null)
            {
                mauh_serial.OnFrameReceived -= OnFrameReceived;
                mauh_serial.Dispose();
            }
            Instance = null;
        }

        private void OnApplicationQuit()
        {
            if (mauh_serial != null)
            {
                mauh_serial.OnFrameReceived -= OnFrameReceived;
                mauh_serial.Dispose();
            }
        }
    }
}
