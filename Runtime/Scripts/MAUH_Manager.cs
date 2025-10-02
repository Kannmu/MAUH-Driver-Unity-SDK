using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO.Ports;

namespace MAUH
{
    internal class MAUH_Manager : MonoBehaviour
    {
        public bool IsConnected { get { return mauh_serial.IsConnected; } }
        public static MAUH_Manager Instance { get; private set; }

        [NonSerialized] public MAUH_Serial mauh_serial;

        [NonSerialized] public float pollingInterval = 0.1f; // 100ms轮询间隔

        [NonSerialized] public System.Action<byte[]> OnDataReceived; // 数据接收事件

        private Coroutine pollingCoroutine;

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
            StartCoroutine(FindDevicePortName());
            Debug.Log("MAUH Device Initialized Successfully.");
        }

        private void Start()
        {
            
        }

        private IEnumerator FindDevicePortName()
        {
            string[] portNames = SerialPort.GetPortNames();
            Debug.Log($"Available ports num: {portNames.Length}, ports: {string.Join(", ", portNames)}");

            if (portNames.Length == 0)
            {
                Debug.LogError("No serial ports available.");
                yield break;
            }

            foreach (string port in portNames)
            {
                string portName = mauh_serial.AttemptToConnectToPort(port);
                if (portName != null)
                {
                    Debug.Log($"Connected to MAUH device on port: {portName}");
                    if (IsConnected)
                    {
                        StartPolling();
                    }
                    yield break;
                }
            }
        }



        public void StartPolling()
        {
            if (pollingCoroutine != null)
            {
                StopCoroutine(pollingCoroutine);
            }
            pollingCoroutine = StartCoroutine(PollingCoroutine());
        }

        public void StopPolling()
        {
            if (pollingCoroutine != null)
            {
                StopCoroutine(pollingCoroutine);
                pollingCoroutine = null;
            }
        }

        private IEnumerator PollingCoroutine()
        {
            while (IsConnected)
            {
                try
                {
                    // 使用非阻塞方式检查数据
                    byte[] receivedData = mauh_serial.TryReadFrame();

                    if (receivedData != null)
                    {
                        // 触发数据接收事件
                        OnDataReceived?.Invoke(receivedData);

                        // 处理接收到的数据
                        ProcessReceivedData(receivedData);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"轮询过程中发生错误: {ex.Message}");
                }

                // 等待下一次轮询
                yield return new WaitForSeconds(pollingInterval);
            }
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
                        Debug.Log("收到Ping ACK");
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
            if (mauh_serial.Connect())
            {
                StartPolling();
            }
        }


















        private void OnDestroy()
        {
            mauh_serial?.Dispose();
            Instance = null;
        }
        private void OnApplicationQuit()
        {
            mauh_serial?.Dispose();
        }
    }
}