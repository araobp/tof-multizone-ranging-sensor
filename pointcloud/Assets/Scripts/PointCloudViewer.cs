using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using TMPro;
using UnityEngine;

public class PointCloudViewer : MonoBehaviour
{
    [Header("Prefabs & Materials")]
    [SerializeField] private GameObject pointPrefab;
    [SerializeField] private Material lineMaterial;

    [Header("Settings")]
    [SerializeField] private float pointRadius = 0.01f;
    [SerializeField] private float lineRadius = 0.002f;
    [SerializeField] private int pointCount = 64; // Enforced 64 points for 8x8 matrix

    [Header("Orientation Settings")]
    [SerializeField] private bool flipVertical = true;
    [SerializeField] private bool flipHorizontal = true;

    [Header("Cutoff Filter Settings")]
    [SerializeField] private float cutoffThreshold = 0.5f; // Default 0.5m
    [SerializeField] private bool enableCutoff = true;
    [SerializeField] private TMP_Dropdown cutoffDropdown;

    [Header("Serial Port Settings")]
    [SerializeField] private bool useSerialInput = true;
    [SerializeField] private string portName = "";
    [SerializeField] private int baudRate = 115200;

    private readonly List<GameObject> points = new List<GameObject>();
    private readonly List<GameObject> lines = new List<GameObject>();
    private readonly float[] cutoffOptions = new float[] { 0.5f, 1.0f, 1.5f, 2.0f, 2.5f, 3.0f, 3.5f, 4.0f, 999.0f };

    // Serial communications
    private const byte BeginByte = 0xFE;
    private const byte EndByte = 0xFF;
    private SimpleSerialPort serialPort;
    private Thread serialThread;
    private volatile bool isSerialRunning;
    private readonly object lockObject = new object();
    private byte[] latestFrameData;
    private int receivedFrameCount = 0;

    private void Awake()
    {
        // Force pointCount to 64 to guarantee full 8x8 matrix (8 rows x 8 cols)
        pointCount = 64;
    }

    private void Start()
    {
        // Automatically attach CameraController to Main Camera if missing
        if (Camera.main != null && Camera.main.GetComponent<CameraController>() == null)
        {
            Camera.main.gameObject.AddComponent<CameraController>();
        }

        // Bind to existing TMP_Dropdown placed on Canvas
        SetupCanvasDropdown();

        // Initialize 64 points and lines for full 8x8 matrix at initial depth d = 0.3m (within default 0.5m cutoff)
        InitializePointCloud(64, 0.3f);

        if (useSerialInput)
        {
            StartSerialReader();
        }
    }

    private void Update()
    {
        if (!useSerialInput) return;

        byte[] frame = null;
        lock (lockObject)
        {
            if (latestFrameData != null)
            {
                frame = latestFrameData;
                latestFrameData = null;
            }
        }

        if (frame != null)
        {
            UpdatePointCloudFromSerial(frame);
        }
    }

    private void OnDestroy()
    {
        StopSerialReader();
    }

    private void OnApplicationQuit()
    {
        StopSerialReader();
    }

    // Bind to existing TMP_Dropdown component on Canvas
    private void SetupCanvasDropdown()
    {
        // 1. Ensure EventSystem exists and has valid Input Module for UI clicks
        EnsureEventSystem();

        // 2. Find TMP_Dropdown if not assigned in Inspector
        if (cutoffDropdown == null)
        {
            cutoffDropdown = FindObjectOfType<TMP_Dropdown>();
        }

        if (cutoffDropdown != null)
        {
            // Ensure Canvas has GraphicRaycaster for click interaction
            Canvas canvas = cutoffDropdown.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }

            cutoffDropdown.interactable = true;
            cutoffDropdown.onValueChanged.RemoveListener(OnCutoffDropdownChanged);

            // Populate options if list is empty or unconfigured
            if (cutoffDropdown.options == null || cutoffDropdown.options.Count == 0)
            {
                cutoffDropdown.options.Clear();
                cutoffDropdown.options.Add(new TMP_Dropdown.OptionData("0.5m (Default)"));
                cutoffDropdown.options.Add(new TMP_Dropdown.OptionData("1.0m"));
                cutoffDropdown.options.Add(new TMP_Dropdown.OptionData("1.5m"));
                cutoffDropdown.options.Add(new TMP_Dropdown.OptionData("2.0m"));
                cutoffDropdown.options.Add(new TMP_Dropdown.OptionData("2.5m"));
                cutoffDropdown.options.Add(new TMP_Dropdown.OptionData("3.0m"));
                cutoffDropdown.options.Add(new TMP_Dropdown.OptionData("3.5m"));
                cutoffDropdown.options.Add(new TMP_Dropdown.OptionData("4.0m"));
                cutoffDropdown.options.Add(new TMP_Dropdown.OptionData("OFF (No Limit)"));
            }

            // Parse initial selection
            int initialIndex = cutoffDropdown.value;
            if (initialIndex >= 0 && initialIndex < cutoffDropdown.options.Count)
            {
                cutoffThreshold = ParseCutoffFromText(cutoffDropdown.options[initialIndex].text, initialIndex);
            }
            else
            {
                cutoffDropdown.value = 0;
                cutoffThreshold = 0.5f;
            }

            cutoffDropdown.RefreshShownValue();
            cutoffDropdown.onValueChanged.AddListener(OnCutoffDropdownChanged);

            Debug.Log($"[PointCloudGenerator] Successfully bound to Canvas TMP_Dropdown '{cutoffDropdown.gameObject.name}'. Initial Threshold: {cutoffThreshold}m");
        }
        else
        {
            Debug.LogWarning("[PointCloudGenerator] Cutoff TMP_Dropdown not found on Canvas. Please assign it in Inspector or place a TMP_Dropdown on Canvas.");
        }
    }

    private void EnsureEventSystem()
    {
        var eventSystem = FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
        if (eventSystem == null)
        {
            GameObject eventSys = new GameObject("EventSystem");
            eventSystem = eventSys.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if ENABLE_INPUT_SYSTEM
            eventSys.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            eventSys.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
            Debug.Log("[PointCloudGenerator] Created missing EventSystem for UI interaction.");
        }
        else
        {
#if ENABLE_INPUT_SYSTEM
            var legacyModule = eventSystem.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            if (legacyModule != null)
            {
                DestroyImmediate(legacyModule);
            }
            if (eventSystem.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }
#endif
        }
    }

    private void OnCutoffDropdownChanged(int index)
    {
        if (cutoffDropdown != null && index >= 0 && index < cutoffDropdown.options.Count)
        {
            string text = cutoffDropdown.options[index].text;
            cutoffThreshold = ParseCutoffFromText(text, index);
            Debug.Log($"[PointCloudGenerator] Dropdown selected index {index}: '{text}' -> Set Cutoff Threshold = {cutoffThreshold}m");
        }
        else if (index >= 0 && index < cutoffOptions.Length)
        {
            cutoffThreshold = cutoffOptions[index];
            Debug.Log($"[PointCloudGenerator] Dropdown selected index {index} -> Set Cutoff Threshold = {cutoffThreshold}m");
        }

        // Instantly re-evaluate visibility for all points and lines
        ApplyCutoffFilter();
    }

    private float ParseCutoffFromText(string text, int fallbackIndex)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (fallbackIndex >= 0 && fallbackIndex < cutoffOptions.Length) ? cutoffOptions[fallbackIndex] : 0.5f;
        }

        string lower = text.ToLower().Trim();
        if (lower.Contains("off") || lower.Contains("limit") || lower.Contains("none") || lower.Contains("all") || lower.Contains("max"))
        {
            return 999.0f;
        }

        var match = Regex.Match(text, @"\d+(\.\d+)?");
        if (match.Success && float.TryParse(match.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedVal))
        {
            return parsedVal;
        }

        return (fallbackIndex >= 0 && fallbackIndex < cutoffOptions.Length) ? cutoffOptions[fallbackIndex] : 0.5f;
    }

    private void ApplyCutoffFilter()
    {
        for (int n = 0; n < points.Count; n++)
        {
            if (points[n] != null)
            {
                float dist = points[n].transform.position.magnitude;
                bool isVisible = !enableCutoff || (dist <= cutoffThreshold);
                points[n].SetActive(isVisible);

                if (n < lines.Count && lines[n] != null)
                {
                    lines[n].SetActive(isVisible);
                }
            }
        }
    }

    // Initialize specified number of points and lines (64 for 8x8 matrix)
    private void InitializePointCloud(int count, float initialDepth)
    {
        ClearPointCloud();

        for (int n = 0; n < count; n++)
        {
            Vector3 pos = GenCoordsFor8x8Matrix(initialDepth, n);
            GameObject pointObj = AddPoint(pos, pointRadius);
            GameObject lineObj = DrawLine3D(Vector3.zero, pos, Color.cyan, 2.0f);
            UpdatePointAndLine(n, pos, initialDepth);
        }
    }

    // Update 3D positions of points and lines from serial byte array payload
    private void UpdatePointCloudFromSerial(byte[] frame)
    {
        receivedFrameCount++;
        if (receivedFrameCount == 1 || receivedFrameCount % 100 == 0)
        {
            Debug.Log($"[PointCloudGenerator] Received frame #{receivedFrameCount}: {frame.Length} bytes. Sample z[0]: {frame[0]} cm");
        }

        bool is16ZonePayload = (frame.Length == 16);

        for (int n = 0; n < points.Count; n++) // points.Count is 64 (8x8)
        {
            int row = n / 8;
            int col = n % 8;

            // Apply vertical / horizontal flip correction
            int targetRow = flipVertical ? (7 - row) : row;
            int targetCol = flipHorizontal ? (7 - col) : col;

            int srcIdx;
            if (is16ZonePayload)
            {
                int r4 = targetRow / 2;
                int c4 = targetCol / 2;
                srcIdx = Mathf.Clamp(r4 * 4 + c4, 0, 15);
            }
            else
            {
                int mappedIndex = targetRow * 8 + targetCol;
                srcIdx = Mathf.Clamp(mappedIndex, 0, frame.Length - 1);
            }

            // Convert byte distance (cm: 0-200) to meters (0-2.0m)
            float cm = frame[srcIdx];
            float dInMeters = cm / 100.0f;
            if (dInMeters <= 0.001f) dInMeters = 0.01f;

            Vector3 pos = GenCoordsFor8x8Matrix(dInMeters, n);
            UpdatePointAndLine(n, pos, dInMeters);
        }
    }

    private void UpdatePointAndLine(int index, Vector3 pos, float distanceInMeters)
    {
        bool isVisible = !enableCutoff || (distanceInMeters <= cutoffThreshold);

        if (index < points.Count && points[index] != null)
        {
            points[index].SetActive(isVisible);
            if (isVisible)
            {
                points[index].transform.position = pos;
            }
        }

        if (index < lines.Count && lines[index] != null)
        {
            lines[index].SetActive(isVisible);
            if (isVisible)
            {
                LineRenderer lr = lines[index].GetComponent<LineRenderer>();
                if (lr != null)
                {
                    lr.SetPosition(0, Vector3.zero);
                    lr.SetPosition(1, pos);
                }
            }
        }
    }

    public GameObject AddPoint(Vector3 pos, float r)
    {
        GameObject instance;
        if (pointPrefab != null)
        {
            instance = Instantiate(pointPrefab, pos, Quaternion.identity, transform);
        }
        else
        {
            instance = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            instance.transform.SetParent(transform);
            instance.transform.position = pos;
        }

        float s = 2.0f * r;
        instance.transform.localScale = new Vector3(s, s, s);

        points.Add(instance);
        return instance;
    }

    public GameObject DrawLine3D(Vector3 a, Vector3 b, Color color, float glowEnergy = 2.0f)
    {
        GameObject lineObj = new GameObject($"Line_{lines.Count}");
        lineObj.transform.SetParent(transform);

        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);

        float width = lineRadius * 2.0f;
        lr.startWidth = width;
        lr.endWidth = width;
        lr.useWorldSpace = true;

        if (lineMaterial != null)
        {
            // Use the specified line material directly
            lr.sharedMaterial = lineMaterial;
        }
        else
        {
            // Fallback configuration if no line material is specified
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Unlit/Color");
            Material mat = new Material(shader)
            {
                color = color
            };
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * glowEnergy);
            }

            lr.material = mat;
            lr.startColor = color;
            lr.endColor = color;
        }

        lines.Add(lineObj);
        return lineObj;
    }

    public Vector3 GenCoordsFor8x8Matrix(float d, int n)
    {
        int m = n % 8;

        // Equal-angular step calculation
        float thetaX = Mathf.Deg2Rad * (-5.625f * (m - 4) - 2.8125f);
        float thetaY = Mathf.Deg2Rad * (5.625f * ((n - m) / 8 - 4) + 2.8125f);

        // Tangent calculation adapted to VL53L5CX firmware characteristics.
        // The reported distance 'd' serves directly as the Z-axis (depth).
        float z = d;
        float x = z * Mathf.Tan(thetaX);
        float y = z * Mathf.Tan(thetaY);

        return new Vector3(x, y, z);
    }

    public void ClearPointCloud()
    {
        foreach (var p in points)
        {
            if (p != null) Destroy(p);
        }
        points.Clear();

        foreach (var l in lines)
        {
            if (l != null) Destroy(l);
        }
        lines.Clear();
    }

    // --- Serial Communication ---

    private void StartSerialReader()
    {
        string[] allPorts = SimpleSerialPort.GetAllPorts();
        if (allPorts != null && allPorts.Length > 0)
        {
            Debug.Log($"[PointCloudGenerator] Detected serial ports: {string.Join(", ", allPorts)}");
        }
        else
        {
            Debug.LogWarning("[PointCloudGenerator] No serial ports found on system.");
        }

        string targetPort = string.IsNullOrEmpty(portName) ? SimpleSerialPort.AutoFindPort() : portName;
        if (string.IsNullOrEmpty(targetPort))
        {
            Debug.LogWarning("[PointCloudGenerator] No matching Arduino / USB Serial port found.");
            return;
        }

        serialPort = new SimpleSerialPort();
        if (!serialPort.Open(targetPort, baudRate))
        {
            Debug.LogError($"[PointCloudGenerator] Failed to open serial port {targetPort}");
            return;
        }

        Debug.Log($"[PointCloudGenerator] Connected to USB Serial port: {targetPort} at {baudRate} baud.");

        isSerialRunning = true;
        serialThread = new Thread(ReadSerialLoop)
        {
            IsBackground = true
        };
        serialThread.Start();
    }

    private void ReadSerialLoop()
    {
        List<byte> payloadBuffer = new List<byte>();

        while (isSerialRunning && serialPort != null && serialPort.IsOpen)
        {
            try
            {
                int b = serialPort.ReadByte();
                if (b == -1) continue;

                byte val = (byte)b;

                if (val == BeginByte)
                {
                    payloadBuffer.Clear();
                }
                else if (val == EndByte)
                {
                    if (payloadBuffer.Count > 0)
                    {
                        byte[] frame = payloadBuffer.ToArray();
                        lock (lockObject)
                        {
                            latestFrameData = frame;
                        }
                    }
                    payloadBuffer.Clear();
                }
                else
                {
                    payloadBuffer.Add(val);
                }
            }
            catch (TimeoutException)
            {
                // Timeout is expected when waiting for next frame
            }
            catch (Exception e)
            {
                if (isSerialRunning)
                {
                    Debug.LogWarning($"[PointCloudGenerator] Serial read exception: {e.Message}");
                }
                break;
            }
        }
    }

    private void StopSerialReader()
    {
        isSerialRunning = false;

        if (serialPort != null)
        {
            try
            {
                serialPort.Close();
            }
            catch { }
            serialPort = null;
        }

        if (serialThread != null && serialThread.IsAlive)
        {
            try
            {
                serialThread.Join(500);
            }
            catch { }
            serialThread = null;
        }
    }
}

// Reflection-based helper to access System.IO.Ports.SerialPort without static compile errors (CS0246)
public class SimpleSerialPort
{
    private static Type serialPortType;
    private object portInstance;
    private Stream baseStream;
    private PropertyInfo isOpenProp;
    private MethodInfo closeMethod;

    public static Type GetSerialPortType()
    {
        if (serialPortType != null) return serialPortType;

        serialPortType = Type.GetType("System.IO.Ports.SerialPort, System.IO.Ports")
                      ?? Type.GetType("System.IO.Ports.SerialPort, System");

        if (serialPortType == null)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("System.IO.Ports.SerialPort");
                if (t != null)
                {
                    serialPortType = t;
                    break;
                }
            }
        }
        return serialPortType;
    }

    public static string[] GetAllPorts()
    {
        Type type = GetSerialPortType();
        if (type == null) return new string[0];

        var method = type.GetMethod("GetPortNames", BindingFlags.Public | BindingFlags.Static);
        if (method == null) return new string[0];

        return method.Invoke(null, null) as string[] ?? new string[0];
    }

    public static string AutoFindPort()
    {
        string[] ports = GetAllPorts();
        if (ports == null || ports.Length == 0) return null;

        // Filter out Bluetooth and internal incoming ports
        List<string> candidates = new List<string>();
        foreach (string port in ports)
        {
            string lower = port.ToLower();
            if (lower.Contains("bluetooth") || lower.Contains("incoming") || lower.Contains("wlan") || lower.Contains("airpods"))
            {
                continue;
            }
            candidates.Add(port);
        }

        if (candidates.Count == 0) return null;

        // Priority USB-Serial hardware keywords
        string[] keywords = new string[]
        {
            "usbmodem",
            "usbserial",
            "wchusbserial",
            "slab_usbtouart",
            "ch340",
            "cp210",
            "ftdi",
            "arduino",
            "acm",
            "usb"
        };

        // 1. Prefer cu.* ports matching USB keywords (macOS standard for outgoing serial)
        foreach (string port in candidates)
        {
            string lower = port.ToLower();
            if (lower.Contains("cu."))
            {
                foreach (string kw in keywords)
                {
                    if (lower.Contains(kw)) return port;
                }
            }
        }

        // 2. Any port matching USB keywords
        foreach (string port in candidates)
        {
            string lower = port.ToLower();
            foreach (string kw in keywords)
            {
                if (lower.Contains(kw)) return port;
            }
        }

        // 3. Prefer cu.* over tty.*
        foreach (string port in candidates)
        {
            if (port.ToLower().Contains("cu.")) return port;
        }

        return candidates[0];
    }

    public bool Open(string portName, int baudRate, int readTimeout = 1000)
    {
        Type type = GetSerialPortType();
        if (type == null)
        {
            Debug.LogError("[SimpleSerialPort] System.IO.Ports.SerialPort type not found in runtime assemblies.");
            return false;
        }

        try
        {
            portInstance = Activator.CreateInstance(type, new object[] { portName, baudRate });

            // Enable DTR & RTS for Arduino native USB stability
            var dtrProp = type.GetProperty("DtrEnable");
            if (dtrProp != null) dtrProp.SetValue(portInstance, true);

            var rtsProp = type.GetProperty("RtsEnable");
            if (rtsProp != null) rtsProp.SetValue(portInstance, true);

            var timeoutProp = type.GetProperty("ReadTimeout");
            if (timeoutProp != null) timeoutProp.SetValue(portInstance, readTimeout);

            var openMethod = type.GetMethod("Open");
            closeMethod = type.GetMethod("Close");
            isOpenProp = type.GetProperty("IsOpen");

            openMethod?.Invoke(portInstance, null);

            var baseStreamProp = type.GetProperty("BaseStream");
            if (baseStreamProp != null)
            {
                baseStream = baseStreamProp.GetValue(portInstance) as Stream;
            }

            return IsOpen;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SimpleSerialPort] Failed to open port {portName}: {e.Message}");
            return false;
        }
    }

    public bool IsOpen
    {
        get
        {
            if (portInstance == null || isOpenProp == null) return false;
            try
            {
                return (bool)isOpenProp.GetValue(portInstance);
            }
            catch
            {
                return false;
            }
        }
    }

    public int ReadByte()
    {
        if (baseStream != null)
        {
            return baseStream.ReadByte();
        }
        return -1;
    }

    public void Close()
    {
        if (baseStream != null)
        {
            try { baseStream.Close(); } catch { }
            baseStream = null;
        }

        if (portInstance != null && closeMethod != null)
        {
            try
            {
                if (IsOpen)
                {
                    closeMethod.Invoke(portInstance, null);
                }
            }
            catch { }
        }
        portInstance = null;
    }
}
