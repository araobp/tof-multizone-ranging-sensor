using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PointCloudGenerator : MonoBehaviour
{
    [Header("Prefabs & Materials")]
    [SerializeField] private GameObject pointPrefab;
    [SerializeField] private Material lineMaterial;

    [Header("Settings")]
    [SerializeField] private float pointRadius = 0.01f;
    [SerializeField] private float lineRadius = 0.002f;

    private readonly List<GameObject> points = new List<GameObject>();
    private readonly List<GameObject> lines = new List<GameObject>();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private IEnumerator Start()
    {
        if (Camera.main != null && Camera.main.GetComponent<CameraController>() == null)
        {
            Camera.main.gameObject.AddComponent<CameraController>();
        }

        float d = 1.0f;
        for (int n = 0; n < 64; n++)
        {
            Vector3 pos = GenCoordsFor8x8Matrix(d, n);
            Debug.Log($"[{d},{n}] {pos}");
            AddPoint(pos, pointRadius);
            DrawLine3D(Vector3.zero, pos, Color.cyan, 2.0f);
            yield return new WaitForSeconds(1.0f);
        }
    }

    // Update is called once per frame
    private void Update()
    {
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
}
