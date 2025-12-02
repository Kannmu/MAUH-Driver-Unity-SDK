using UnityEngine;

namespace UMH
{
    public static class UMH_API
    {
        public static bool IsConnected => UMH_Manager.Instance != null && UMH_Manager.Instance.IsConnected;

        public static void Connect(string portName, int baudRate)
        {
            if (UMH_Manager.Instance != null)
            {
                UMH_Manager.Instance.Connect(portName, baudRate);
            }
        }
    }
}
