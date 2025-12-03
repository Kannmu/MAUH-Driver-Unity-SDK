using System.Collections;
using UnityEngine;
using UMH;
using TMPro;

public class UMHController : MonoBehaviour
{

    private Transform _content;
    // Start is called before the first frame update
    void Start()
    {
        _content = transform.Find("Canvas").Find("Content");
        StartCoroutine(UpdateDeviceStatus());
    }

    // Update is called once per frame
    void Update()
    {

    }

    private IEnumerator UpdateDeviceStatus()
    {

        while (true)
        {
            UMH_Device_Status status = UMH_API.DeviceStatus;
            if (status != null)
            {
                _content.Find("Voltage").GetComponent<TextMeshProUGUI>().text = $"Voltage: {status.Voltage:F2} V";
                _content.Find("Temperature").GetComponent<TextMeshProUGUI>().text = $"Temperature: {status.Temperature:F2} °C";
            }

            yield return new WaitForSeconds(1.0f);
        }
    }


}
