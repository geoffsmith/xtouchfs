namespace FSKontrol.WPF
{
    class MidiSimControl
    {
        public MidiSimControl(MidiControlType controlType, int controlId, Field field)
        {
            ControlType = controlType;
            ControlId = controlId;
            Field = field;
            // Set up a SimControlAdaptor
            // Get the initial value
            // Set up the lighting
            // Listen for changes to the value
            // Set up midi handling to update the value
        }

        public MidiControlType ControlType { get; }
        public int ControlId { get; }
        public Field Field { get; }
    }

    enum MidiControlType
    {
        Fader,
        Button,
        Encoder,
    }
}
