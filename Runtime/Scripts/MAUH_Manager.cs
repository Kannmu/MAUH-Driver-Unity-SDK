using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO.Ports;

namespace MAUH
{
    internal class MAUH_Manager : MonoBehaviour
    {
        public bool IsConnected { get { return mauh_serial.IsConnected; } }
        public static MAUH_Manager Instance { get; private set; }

        public MAUH_Serial mauh_serial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitializeOnLoad()
        {
            if (Instance != null)
            {
                return;
            }

            // 创建一个新的、隐藏的 GameObject 来承载我们的管理器。
            GameObject managerObject = new GameObject("[MAUH_SerialManager]");
            Instance = managerObject.AddComponent<MAUH_Manager>();

            // （可选但推荐）隐藏这个对象，让 Hierarchy 面板保持整洁。
            managerObject.hideFlags = HideFlags.HideInHierarchy;

            Debug.Log("MAUH Device Initialized Successfully.");
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this.gameObject);
                return;
            }
            Instance = this;
            mauh_serial = new MAUH_Serial();
        }
        





    }
}