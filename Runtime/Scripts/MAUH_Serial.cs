using System.IO.Ports;
using System;

namespace MAUH
{
    public class MAUH_Serial : IDisposable
    {
        private bool isConnected = false;
        public bool IsConnected
        {
            get { return isConnected; }
            private set { isConnected = value; }
        }

        private string portName = "COM3";
        public string PortName
        {
            get { return portName; }
            set { portName = value; }
        }

        private readonly int[] validBaudRates = new int[] { 9600, 19200, 38400, 57600, 115200 };

        private int baudRate = 115200;
        public int BaudRate
        {
            get { return baudRate; }
            set
            {
                if (Array.IndexOf(validBaudRates, value) == -1)
                {
                    Console.WriteLine("BaudRate must be one of 9600, 19200, 38400, 57600, 115200");
                    return;
                }
                baudRate = value;
            }
        }

        private SerialPort serialPort;

        public MAUH_Serial()
        {

        }

        public bool Connect()
        {
            try
            {
                serialPort = new SerialPort(portName, baudRate);
                serialPort.Open();
                IsConnected = serialPort.IsOpen;
                return IsConnected;
            }
            catch (Exception ex)
            {
                // 处理连接错误
                Console.WriteLine($"Error opening serial port: {ex.Message}");
                IsConnected = false;
                return false;
            }
        }

        
        public void Dispose()
        {
            if (serialPort != null && serialPort.IsOpen)
            {
                serialPort.Close();
            }
        }
    }





}







