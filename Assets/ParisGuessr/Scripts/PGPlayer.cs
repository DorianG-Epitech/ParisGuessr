using UnityEngine;

/// First-person walker. Desktop: click to capture the mouse, WASD/ZQSD + mouse, Shift to run.
/// Touch: floating joystick on the left half, drag on the right half to look.
[RequireComponent(typeof(CharacterController))]
public class PGPlayer : MonoBehaviour
{
    public Camera cam;
    public float walk = 4.5f, run = 10f, lookSens = 2.0f, touchLook = 0.22f;
    public bool controlsEnabled;
    public float yaw, pitch;
    CharacterController cc; float vy;
    int joyId = -99, lookId = -99; Vector2 joyStart, joyPos;
    public static Vector2 JoyCenter, JoyKnob; public static bool JoyActive;

    void Awake() { cc = GetComponent<CharacterController>(); }

    public void Teleport(Vector3 p, float heading)
    {
        cc.enabled = false; transform.position = p; cc.enabled = true;
        yaw = heading; pitch = 0; vy = 0; Apply();
    }

    void Apply()
    {
        transform.rotation = Quaternion.Euler(0, yaw, 0);
        cam.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
    }

    void Update()
    {
        PGInput.Poll();
        Vector2 move = Vector2.zero; bool running = false;
        if (controlsEnabled)
        {
            if (!PGInput.TouchMode)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                bool locked = Cursor.lockState == CursorLockMode.Locked;
#else
                bool locked = Cursor.lockState == CursorLockMode.Locked || Application.isBatchMode;
#endif
                if (Input.GetMouseButtonDown(0) && !locked && !PGInput.Consumed.Contains(-1)) Cursor.lockState = CursorLockMode.Locked;
                if (locked)
                {
                    yaw += Input.GetAxisRaw("Mouse X") * lookSens; pitch -= Input.GetAxisRaw("Mouse Y") * lookSens;
                }
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Z) || Input.GetKey(KeyCode.UpArrow)) move.y += 1;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) move.y -= 1;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftArrow)) move.x -= 1;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move.x += 1;
                running = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            }
            else Touch(ref move, ref running);
        }
        else { joyId = lookId = -99; JoyActive = false; }
        pitch = Mathf.Clamp(pitch, -80, 80); Apply();
        var dir = transform.forward * move.y + transform.right * move.x;
        if (dir.sqrMagnitude > 1) dir.Normalize();
        float sp = running ? run : walk;
        vy = cc.isGrounded ? -2f : vy - 20f * Time.deltaTime;
        cc.Move((dir * sp + Vector3.up * vy) * Time.deltaTime);
        // keep inside the world box
        var p = transform.position;
        float cx = Mathf.Clamp(p.x, 60, PGWorld.Width - 60), cz = Mathf.Clamp(p.z, 60, PGWorld.Height - 60);
        if (cx != p.x || cz != p.z) { cc.enabled = false; transform.position = new Vector3(cx, p.y, cz); cc.enabled = true; }
    }

    void Touch(ref Vector2 move, ref bool running)
    {
        float W = Screen.width / PGInput.Scale;
        foreach (var t in PGInput.Pointers)
        {
            if (PGInput.Consumed.Contains(t.id)) continue;
            if (t.phase == TouchPhase.Began)
            {
                if (t.pos.x < W * 0.42f && joyId == -99) { joyId = t.id; joyStart = t.pos; joyPos = t.pos; }
                else if (lookId == -99) lookId = t.id;
            }
            else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
            {
                if (t.id == joyId) joyId = -99; if (t.id == lookId) lookId = -99;
            }
            else
            {
                if (t.id == joyId) joyPos = t.pos;
                if (t.id == lookId) { yaw += t.delta.x * touchLook; pitch += t.delta.y * touchLook; }
            }
        }
        JoyActive = joyId != -99;
        if (JoyActive)
        {
            var d = joyPos - joyStart; float R = 60f;
            if (d.magnitude > R) d = d.normalized * R;
            JoyCenter = joyStart; JoyKnob = joyStart + d;
            move = new Vector2(d.x, -d.y) / R;
            running = move.magnitude > 0.92f;
        }
    }
}
