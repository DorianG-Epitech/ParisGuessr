using System.Collections.Generic;
using UnityEngine;

/// Unified pointers (touches or mouse) in GUI space (top-left origin, divided by the UI scale).
public static class PGInput
{
    public struct P { public int id; public TouchPhase phase; public Vector2 pos, delta; }
    public static readonly List<P> Pointers = new List<P>();
    public static bool TouchMode;
    public static float Scale = 1;
    static int frame = -1;
    static Vector2 lastMouse;

    public static Vector2 ToGui(Vector2 screen) => new Vector2(screen.x, Screen.height - screen.y) / Scale;

    public static void Poll()
    {
        if (frame == Time.frameCount) return; frame = Time.frameCount;
        var prev = new List<int>(); foreach (var q in Pointers) if (q.phase == TouchPhase.Ended || q.phase == TouchPhase.Canceled) prev.Add(q.id);
        foreach (var id in prev) Consumed.Remove(id);
        Pointers.Clear();
        if (Input.touchCount > 0) TouchMode = true;
        if (TouchMode)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                Pointers.Add(new P { id = t.fingerId, phase = t.phase, pos = ToGui(t.position), delta = new Vector2(t.deltaPosition.x, -t.deltaPosition.y) / Scale });
            }
            return;
        }
        Vector2 m = Input.mousePosition;
        var gp = ToGui(m); var d = (m - lastMouse); lastMouse = m;
        if (Input.GetMouseButtonDown(0)) Pointers.Add(new P { id = -1, phase = TouchPhase.Began, pos = gp });
        else if (Input.GetMouseButtonUp(0)) Pointers.Add(new P { id = -1, phase = TouchPhase.Ended, pos = gp, delta = new Vector2(d.x, -d.y) / Scale });
        else if (Input.GetMouseButton(0)) Pointers.Add(new P { id = -1, phase = TouchPhase.Moved, pos = gp, delta = new Vector2(d.x, -d.y) / Scale });
    }

    /// True if a pointer began inside r this frame (the pointer is then "consumed" for other handlers via the returned id).
    public static bool Tapped(Rect r)
    {
        Poll();
        foreach (var p in Pointers) if (p.phase == TouchPhase.Began && r.Contains(p.pos)) { Consumed.Add(p.id); return true; }
        return false;
    }
    public static readonly HashSet<int> Consumed = new HashSet<int>();
}
