namespace WeatherManager
{
    /// <summary>One press owns one interaction until release, including tracking interruptions.</summary>
    public sealed class WeatherPressGate
    {
        private bool wasPressed;
        private bool blocked;
        private bool ownsWorld;
        public bool OwnsWorld => ownsWorld;

        public bool Read(bool pressed, bool overUi, bool validTarget, bool paused, out bool begin)
        {
            begin = false;
            if (!pressed)
            {
                wasPressed = blocked = ownsWorld = false;
                return false;
            }
            if (overUi || !validTarget || paused)
            {
                blocked = true;
                ownsWorld = false;
            }
            if (!wasPressed && !blocked)
            {
                ownsWorld = true;
                begin = true;
            }
            wasPressed = true;
            return ownsWorld && !blocked;
        }

        public void RequireRelease()
        {
            blocked = true;
            ownsWorld = false;
        }
    }
}
