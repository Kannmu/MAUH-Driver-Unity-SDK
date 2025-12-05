using System;
using System.Threading.Tasks;
using UnityEngine;

namespace UMH
{

    public class UMH_Device_Status
    {
        public float Voltage { get; set; }
        public float Temperature { get; set; }
        public float LoopFreq { get; set; }
        public int CalibrationMode { get; set; }
        public int SimulationMode { get; set; }
    }
    public class UMH_Point
    {
        public Vector3 Position { get; set; } = new Vector3(0, 0, 0); // Position of the Focus Point in meters
        public float Strength { get; set; } // Strength of the Focus Point
        public Vector3 Vibration { get; set; } = new Vector3(0, 0, 0); // Vibration Distance for each axis in meters
        public float Frequency { get; set; } = 200f; // Vibration Frequency in Hz
    }

    public static class UMH_API
    {
        public static Vector3 Position { get; set; } = new Vector3(0, 0, 0); // Current Position of the Focus Point in meters
        public static double UpdateDeltaTime { get; set; } = 0.0f; // Current Update Delta Time in seconds
        public static bool IsConnected => UMH_Manager.Instance != null && UMH_Manager.Instance.IsConnected;
        public static UMH_Device_Status DeviceStatus { get; private set; } = new UMH_Device_Status();
        public static void ManualConnect(string portName, int baudRate)
        {
            UMH_Manager.Instance?.Connect(portName, baudRate);
        }

        public static void Reconnect()
        {
            UMH_Manager.Instance?.Reconnect();
        }

        public static void SetRefreshRate(float refreshRate)
        {
            if (UMH_Manager.Instance != null)
            {
                UMH_Manager.Instance.RefreshRate = refreshRate;
            }
        }

        public static UMH_Device_Status GetDeviceStatus()
        {
            return DeviceStatus;
        }

        public static void SendGetStatusCommand()
        {
            UMH_Manager.Instance?.GetStatusAsync();
        }
        public static void HandleStatusUpdate(UMH_Device_Status newStatus)
        {
            DeviceStatus = newStatus;
        }

        public static void HandlePointSent(Vector3 newPosition)
        {
            Position = newPosition;
        }

        public static void HandlePACKReceived(double updateDeltaTime)
        {
            UpdateDeltaTime = updateDeltaTime;
        }
        public static void SetPoints(UMH_Point point)
        {
            UMH_Manager.Instance?.SetPointAsync(point);
        }
    }
}
