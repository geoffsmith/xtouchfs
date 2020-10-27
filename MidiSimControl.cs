namespace FSKontrol.WPF
{
    class MidiSimControl
    {
        public MidiSimControl(MidiControlType controlType, int controlId, Field definition)
        {
            ControlType = controlType;
            ControlId = controlId;
            Definition = definition;
            // Set up a SimControlAdaptor
            // Get the initial value
            // Set up the lighting
            // Listen for changes to the value
            // Set up midi handling to update the value
        }

        public MidiControlType ControlType { get; }
        public int ControlId { get; }
        public Field Definition { get; }

        public void Initialise(SimControlAdaptor simAdaptor)
        {

        }
    }

    enum MidiControlType
    {
        Fader,
        Button,
        Encoder,
    }
}
