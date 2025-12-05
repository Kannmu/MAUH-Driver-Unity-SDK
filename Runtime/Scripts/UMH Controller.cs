using System.Collections;
using UnityEngine;
using UMH;
using TMPro;
using Codice.Client.Commands;

public class UMHController : MonoBehaviour
{
    [Header("Controller Function Settings")]

    [SerializeField, Tooltip("Enable or disable the UI.")]
    private bool enableUI = true;
    public bool EnableUI
    {
        get => enableUI;
        set
        {
            enableUI = value;
            UpdateUIActivity();
        }
    }
    
    [SerializeField, Tooltip("Enable or disable the demonstration of focus point.")]
    private bool enableFocusPoint = true;
    public bool EnableFocusPoint
    {
        get => enableFocusPoint;
        set
        {
            enableFocusPoint = value;
            UpdateFocusPointActivity();
        }
    }

    [SerializeField, Tooltip("Enable Testing Circle")]
    private bool enableTestingCircle = false;
    public bool EnableTestingCircle
    {
        get => enableTestingCircle;
        set
        {
            enableTestingCircle = value;
            UpdateTestingCircleActivity();
        }
    }

    [Header("Refresh Rates Settings")]

    [SerializeField, Tooltip("Refresh rate for getting device status.")]
    private float getDeviceStatusRate = 30f;

    public float GetDeviceStatusRate
    {
        get => getDeviceStatusRate;
        set
        {
            if (value <= 0.0f)
            {
                Debug.LogError("Refresh rate must be greater than 0.0f");
                return;
            }
            getDeviceStatusRate = value;
            UMH_API.SetRefreshRate(value);
        }
    }

    [SerializeField, Tooltip("Refresh rate for updating UI and focus point.")]
    private float uiUpdateRate = 30.0f;

    private WaitForSeconds _waitUIUpdate;

    public float UIUpdateRate
    {
        get => uiUpdateRate;
        set
        {
            if (value <= 0.0f)
            {
                Debug.LogError("UI update rate must be greater than 0.0f");
                return;
            }
            uiUpdateRate = value;
            _waitUIUpdate = new WaitForSeconds(1.0f / uiUpdateRate);
        }
    }

    private bool _needsUpdate = false;

    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            _needsUpdate = true;
        }
        else
        {
            UpdateUIActivity();
            UpdateFocusPointActivity();
            UpdateTestingCircleActivity();
        }
    }

    private void UpdateUIActivity()
    {
        Transform canvas = transform.Find("Canvas");
        if (canvas != null)
        {
            Transform ui = canvas.Find("UI");
            ui?.gameObject.SetActive(EnableUI);
            // pause the UpdateUI coroutine specifically

            if (EnableUI)
            {
                if (Application.isPlaying && gameObject.activeInHierarchy && _updateUICoroutine == null)
                {
                    _updateUICoroutine = StartCoroutine(UpdateUI());
                }
            }
            else
            {
                if (Application.isPlaying && _updateUICoroutine != null)
                {
                    StopCoroutine(_updateUICoroutine);
                    _updateUICoroutine = null;
                }
            }
        }
    }

    private void UpdateTestingCircleActivity()
    {
        if (EnableTestingCircle)
        {
            if (Application.isPlaying && gameObject.activeInHierarchy && _circleTrajectCoroutine == null)
            {
                _circleTrajectCoroutine = StartCoroutine(CircleTraject());
            }
        }
        else
        {
            if (Application.isPlaying && _circleTrajectCoroutine != null)
            {
                StopCoroutine(_circleTrajectCoroutine);
                _circleTrajectCoroutine = null;
            }
        }
    }

    private void UpdateFocusPointActivity()
    {
        transform.Find("Focus").gameObject.SetActive(EnableFocusPoint);

        if (EnableFocusPoint)
        {
            if (Application.isPlaying && gameObject.activeInHierarchy && _updateFocusCoroutine == null && _focusTransform != null)
            {
                _updateFocusCoroutine = StartCoroutine(UpdateFocus());
            }
        }
        else
        {
            if (Application.isPlaying && _updateFocusCoroutine != null && _focusTransform != null)
            {
                StopCoroutine(_updateFocusCoroutine);
                _updateFocusCoroutine = null;
            }
        }
    }

    private Transform _info;
    private const string _info_num_color = "#3a8b3aff";
    private const string _info_x_axis_color = "#8b3a3aff";
    private const string _info_y_axis_color = "#3a8b3aff";
    private const string _info_z_axis_color = "#3a648bff";

    // Cached UI Components to avoid GetComponent calls in Update loops
    private TextMeshProUGUI _voltageText;
    private TextMeshProUGUI _temperatureText;
    private TextMeshProUGUI _positionText;
    private TextMeshProUGUI _deltaTimeText;
    private TextMeshProUGUI _loopFreqText;
    private TextMeshProUGUI _calibrationModeText;
    private TextMeshProUGUI _simulationModeText;
    private Transform _focusTransform;
    private Coroutine _updateUICoroutine = null, _circleTrajectCoroutine = null, _updateFocusCoroutine = null;

    private void OnEnable()
    {
        if (_waitUIUpdate == null)
            _waitUIUpdate = new WaitForSeconds(1.0f / uiUpdateRate);
        UpdateUIActivity();
        UpdateTestingCircleActivity();
        UpdateFocusPointActivity();
    }

    private void OnDisable()
    {
        _updateUICoroutine = null;
        _circleTrajectCoroutine = null;
        _updateFocusCoroutine = null;
    }

    private void OnAwake()
    {
    }

    // Start is called before the first frame update
    void Start()
    {
        InitializeUIComponents();

        UMH_API.SetRefreshRate(getDeviceStatusRate);

        // Initialize the wait object
        if (_waitUIUpdate == null)
            _waitUIUpdate = new WaitForSeconds(1.0f / uiUpdateRate);
    }

    private void InitializeUIComponents()
    {
        Transform canvas = transform.Find("Canvas");
        if (canvas == null) return;

        Transform ui = canvas.Find("UI");
        if (ui == null) return;

        _info = ui.Find("Info");
        if (_info != null)
        {
            _voltageText = _info.Find("Voltage")?.GetComponent<TextMeshProUGUI>();
            _temperatureText = _info.Find("Temperature")?.GetComponent<TextMeshProUGUI>();
            _positionText = _info.Find("Position")?.GetComponent<TextMeshProUGUI>();
            _deltaTimeText = _info.Find("DeltaTime")?.GetComponent<TextMeshProUGUI>();
            _loopFreqText = _info.Find("LoopFreq")?.GetComponent<TextMeshProUGUI>();
            _calibrationModeText = _info.Find("Calibration")?.GetComponent<TextMeshProUGUI>();
            _simulationModeText = _info.Find("Simulation")?.GetComponent<TextMeshProUGUI>();
        }

        _focusTransform = transform.Find("Focus");
    }

    // Update is called once per frame
    void Update()
    {
        if (_needsUpdate)
        {
            UpdateUIActivity();
            UpdateFocusPointActivity();
            UpdateTestingCircleActivity();
            _needsUpdate = false;
        }
    }

    void FixedUpdate()
    {

    }

    private IEnumerator CircleTraject()
    {
        float radius = 0.04f;
        float freq = 1.0f;
        float angle = 0.0f;
        while (true)
        {
            angle = Time.time % (1 / freq) / (1 / freq) * 2 * Mathf.PI;
            float x = radius * Mathf.Sin(angle);
            float y = radius * Mathf.Cos(angle);
            UMH_Point point = new()
            {
                Position = new Vector3(x, y, 0.05f),
                Strength = 1f,
                Vibration = new Vector3(0.0f, 0.0f, 0.01f),
                Frequency = 200
            };
            UMH_API.SetPoints(point);
            yield return new WaitForSeconds(1.0f / uiUpdateRate);
        }
    }

    

    private IEnumerator UpdateUI()
    {
        while (true)
        {
            if (UMH_API.IsConnected)
            {
                UpdateDeviceStatusUI();
            }
            yield return _waitUIUpdate;
        }
    }
    private IEnumerator UpdateFocus()
    {
        while (true)
        {
            if (UMH_API.IsConnected)
            {
                UpdateFocusPosition();
            }
            yield return _waitUIUpdate;
        }
    }

    private void UpdateDeviceStatusUI()
    {
        UMH_Device_Status status = UMH_API.GetDeviceStatus();

        if (_voltageText != null)
            _voltageText.text = $"Voltage: \t <color={_info_num_color}>{status.Voltage:F2}V</color>";

        if (_temperatureText != null)
            _temperatureText.text = $"Temperature: \t <color={_info_num_color}>{status.Temperature:F2}°C</color>";

        Vector3 pos = UMH_API.Position;
        if (_positionText != null)
            _positionText.text = $"Position: [<color={_info_x_axis_color}>{pos.x:F3}</color>, <color={_info_y_axis_color}>{pos.y:F3}</color>, <color={_info_z_axis_color}>{pos.z:F3}</color>]";

        if (_deltaTimeText != null)
        {
            double dt = UMH_API.UpdateDeltaTime;
            (double dtVal, string dtUnit) = GetTimeWithUnit(dt);
            _deltaTimeText.text = $"DeltaTime: \t <color={_info_num_color}>{dtVal:F3}{dtUnit}</color>";
        }

        if (_loopFreqText != null)
        {
            double freq = status.LoopFreq;
            (double freqVal, string freqUnit) = GetFrequencyWithUnit(freq);
            _loopFreqText.text = $"LoopFreq: \t <color={_info_num_color}>{freqVal:F2}{freqUnit}</color>";
        }

        if (_calibrationModeText != null)
            _calibrationModeText.text = $"Calibration: \t <color={(status.CalibrationMode == 0 ? "#ff0000ff" : "#3a8b3aff")}>{(status.CalibrationMode == 0 ? "Cleaned" : "Calibrated")}</color>";

        if (_simulationModeText != null)
            _simulationModeText.text = $"Simulation: \t <color={(status.SimulationMode == 0 ? "#ff0000ff" : "#3a8b3aff")}>{(status.SimulationMode == 0 ? "Plane" : "Point")}</color>";

        if (status.CalibrationMode == 0 || status.SimulationMode == 0)
        {
            EnableFocusPoint = false;
            EnableTestingCircle = false;
        }
    }

    private void UpdateFocusPosition()
    {
        Vector3 localPos = new(UMH_API.Position.x, UMH_API.Position.z, UMH_API.Position.y);

        if (_focusTransform != null)
            _focusTransform.localPosition = localPos;

        DrawFocusPath(localPos);
    }

    private (double, string) GetTimeWithUnit(double dt)
    {
        (double threshold, string unit, double multiplier)[] scales =
        {
            (1e-9, "ps", 1e12),
            (1e-6, "ns", 1e9 ),
            (1e-3, "μs", 1e6 ),
            (1.0 , "ms", 1e3 ),
            (1e3 , "s" , 1.0  ),
        };

        foreach (var (threshold, unit, multiplier) in scales)
        {
            if (dt < threshold)
                return (dt * multiplier, unit);
        }

        return (dt, "s");
    }

    private (double, string) GetFrequencyWithUnit(double freq)
    {
        (double threshold, string unit)[] scales =
        {
            (1e9, "GHz"),
            (1e6, "MHz"),
            (1e3, "kHz"),
            (1.0, "Hz")
        };

        foreach (var (threshold, unit) in scales)
        {
            if (freq >= threshold)
                return (freq / threshold, unit);
        }

        return (freq, "Hz");
    }

    private void DrawFocusPath(Vector3 position)
    {
        Debug.DrawLine(this.transform.position + new Vector3(0.13f / 2, 0, 0.13f / 2), position + this.transform.position, Color.green, 1.0f / uiUpdateRate);
        Debug.DrawLine(this.transform.position + new Vector3(0.13f / 2, 0, -0.13f / 2), position + this.transform.position, Color.green, 1.0f / uiUpdateRate);
        Debug.DrawLine(this.transform.position + new Vector3(-0.13f / 2, 0, 0.13f / 2), position + this.transform.position, Color.green, 1.0f / uiUpdateRate);
        Debug.DrawLine(this.transform.position + new Vector3(-0.13f / 2, 0, -0.13f / 2), position + this.transform.position, Color.green, 1.0f / uiUpdateRate);
    }

    public void Reconnect()
    {
        UMH_API.Reconnect();
    }
}
