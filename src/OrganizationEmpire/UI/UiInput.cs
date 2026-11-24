using GTA;
using OrganizationEmpire.Native;

namespace OrganizationEmpire.UI
{
    /// <summary>Per-frame mouse state (cursor, clicks, wheel) read from the disabled control inputs.</summary>
    internal sealed class UiInput
    {
        public bool Enabled;
        public bool Click;
        public bool RightClick;
        public int Wheel;
        public bool MouseMoved;
        private float _lastX = -1, _lastY = -1;

        public void Poll(Renderer r, bool mouseEnabled, bool active)
        {
            Click = RightClick = false;
            Wheel = 0;
            MouseMoved = false;
            Enabled = mouseEnabled && active && Nat.UsingKeyboard();
            if (!Enabled)
            {
                r.MouseX = r.MouseY = -1;
                return;
            }
            Nat.ShowCursor();
            float nx = Nat.DisabledNormal(Control.CursorX);
            float ny = Nat.DisabledNormal(Control.CursorY);
            r.MouseX = nx * r.W;
            r.MouseY = ny * r.H;
            if (System.Math.Abs(r.MouseX - _lastX) > 0.5f || System.Math.Abs(r.MouseY - _lastY) > 0.5f) MouseMoved = _lastX >= 0;
            _lastX = r.MouseX;
            _lastY = r.MouseY;
            Click = Nat.DisabledJustPressed(Control.CursorAccept);
            RightClick = Nat.DisabledJustPressed(Control.CursorCancel);
            if (Nat.DisabledJustPressed(Control.CursorScrollUp)) Wheel = 1;
            else if (Nat.DisabledJustPressed(Control.CursorScrollDown)) Wheel = -1;
            if (Click) MouseMoved = true;
        }
    }
}
