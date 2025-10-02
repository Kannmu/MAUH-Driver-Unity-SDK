using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO.Ports;

namespace MAUH
{
    public static class MAUH_API
    {
        public static bool IsConnected => MAUH_Manager.Instance != null && MAUH_Manager.Instance.IsConnected;

        public static void SetPortAndBaudRate(string portName, int baudRate)
        {
            MAUH_Manager.Instance.mauh_serial.PortName = portName;
            MAUH_Manager.Instance.mauh_serial.BaudRate = baudRate;
        }


    }
}

