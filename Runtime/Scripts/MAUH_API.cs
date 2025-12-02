using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MAUH
{
    public static class MAUH_API
    {




        public static bool IsConnected => MAUH_Manager.Instance != null && MAUH_Manager.Instance.IsConnected;

        /// <summary>
        /// Manually connect to the MAUH device through specified port name and baud rate. Usually unnecessary to manually connect because the device will connect automatically when it is detected. 
        /// Baud rate must be one of 9600, 19200, 38400, 57600, 115200.
        /// </summary>
        /// <param name="portName"></param>
        /// <param name="baudRate"></param>
        public static void Connect(string portName, int baudRate)
        {
            MAUH_Manager.Instance.Connect(portName, baudRate);
        }
    }
}

