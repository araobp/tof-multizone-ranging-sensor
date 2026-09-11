using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 2.0f;
    [SerializeField] private float rotateSpeed = 60.0f;
    [SerializeField] private Vector3 pivotPoint = Vector3.zero;

    private void Update()
    {
        float delta = Time.deltaTime;

        bool isPgUp = false;
        bool isPgDn = false;
        bool isRight = false;
        bool isLeft = false;

        // 1. New Input System (Keyboard.current)
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.pageUpKey.isPressed || kb.upArrowKey.isPressed || kb.wKey.isPressed) isPgUp = true;
            if (kb.pageDownKey.isPressed || kb.downArrowKey.isPressed || kb.sKey.isPressed) isPgDn = true;
            if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) isRight = true;
            if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) isLeft = true;
        }

        // 2. Legacy Input Manager (isolated per key to prevent exceptions from blocking input)
        try
        {
            if (Input.GetKey(KeyCode.PageUp) || Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) isPgUp = true;
        }
        catch { }

        try
        {
            if (Input.GetKey(KeyCode.PageDown) || Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) isPgDn = true;
        }
        catch { }

        try
        {
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) isRight = true;
        }
        catch { }

        try
        {
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) isLeft = true;
        }
        catch { }

        // Move Forward (PgUp / UpArrow / W)
        if (isPgUp)
        {
            transform.Translate(Vector3.forward * (moveSpeed * delta), Space.Self);
        }

        // Move Backward (PgDn / DownArrow / S)
        if (isPgDn)
        {
            transform.Translate(-Vector3.forward * (moveSpeed * delta), Space.Self);
        }

        // Rotate around Y-axis at Pivot (RightArrow / D -> Counter-clockwise)
        if (isRight)
        {
            transform.RotateAround(pivotPoint, Vector3.up, rotateSpeed * delta);
        }

        // Rotate around Y-axis at Pivot (LeftArrow / A -> Clockwise)
        if (isLeft)
        {
            transform.RotateAround(pivotPoint, Vector3.up, -rotateSpeed * delta);
        }
    }
}
