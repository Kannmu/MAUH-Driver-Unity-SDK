using System;
using System.Threading.Tasks;
using UnityEngine;

namespace UMH
{
    public class UMH_Device_Status
    {
        public bool IsConnected { get; set; }
        public bool IsEnabled { get; set; }
        public float Voltage { get; set; }
        public float Temperature { get; set; }
    }

    public static class UMH_API
    {
        public static bool IsConnected => UMH_Manager.Instance != null && UMH_Manager.Instance.IsConnected;
        public static UMH_Device_Status DeviceStatus { get; private set; } = new UMH_Device_Status();
        public static void Connect(string portName, int baudRate)
        {
            if (UMH_Manager.Instance != null)
            {
                UMH_Manager.Instance.Connect(portName, baudRate);
            }
        }
        public static void GetStatus()
        {
            if (UMH_Manager.Instance != null)
            {
                UMH_Manager.Instance.GetStatusAsync();
            }
        }
        public static void HandleStatusUpdate(UMH_Device_Status newStatus)
        {
            DeviceStatus = newStatus;
        }
    }
}
