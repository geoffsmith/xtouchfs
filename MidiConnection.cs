using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Devices;
using System;
using System.Collections.Generic;
using System.Reactive.Subjects;

namespace FSKontrol.WPF
{
    /*
     * Lots of information here on controlling the LEDs: 
     * https://stackoverflow.com/questions/39435550/changing-leds-on-x-touch-mini-mackie-control-mc-mode
     */
    class MidiConnection
    {
        private InputDevice inputDevice;
        private OutputDevice outputDevice;
        private List<MidiControlAdaptor> controlAdaptors = new List<MidiControlAdaptor>();

        public MidiConnection()
        {
            inputDevice = InputDevice.GetByName("X-TOUCH MINI");
            inputDevice.EventReceived += OnEventReceived;
            inputDevice.StartEventsListening();
            outputDevice = OutputDevice.GetByName("X-TOUCH MINI");
        }

        private void OnEventReceived(object sender, MidiEventReceivedEventArgs e)
        {
            var midiDevice = (MidiDevice)sender;
            Console.WriteLine($"Event received from '{midiDevice.Name}' at {DateTime.Now}: {e.Event}");
            var map = MapEventToControl(e.Event);
            Console.WriteLine($"Mapped to {map.MidiControlType}:{map.ControlId}");
            foreach (var controlAdaptor in controlAdaptors)
            {
                if (!controlAdaptor.MatchesEvent(map)) continue;
                controlAdaptor.HandleEvent(e.Event);
                break;
            }
        }

        public MidiControlAdaptor CreateAdaptor(MidiControlType controlType, int controlId, SimControlAdaptor simAdaptor)
        {
            var adaptor = new MidiControlAdaptor(controlType, controlId, simAdaptor.UnitType, simAdaptor);
            controlAdaptors.Add(adaptor);
            adaptor.LightControl.Subscribe(HandleLightControl);
            return adaptor;
        }

        private void HandleLightControl(LightControlMessage message)
        {

            var controlNumber = MapControlToLedControlNumber(message.Adaptor.ControlType, message.Adaptor.ControlId);
            var midi = new ControlChangeEvent((SevenBitNumber)controlNumber, (SevenBitNumber)message.ControlValue);
            Console.WriteLine($"Sending midi: {midi}");
            outputDevice.SendEvent(midi);
        }

        private ControlEventMap MapEventToControl(MidiEvent evt)
        {
            MidiControlType controlType = MidiControlType.Fader;
            int controlId = 0;
            if (evt.EventType == MidiEventType.PitchBend)
            {
                controlType = MidiControlType.Fader;
                controlId = 0;
            }
            else if (evt.EventType == MidiEventType.ControlChange)
            {
                var cc = (ControlChangeEvent)evt;
                if (cc.ControlNumber <= 23 && cc.ControlNumber >= 16)
                {
                    controlType = MidiControlType.Encoder;
                    controlId = cc.ControlNumber - 16;
                }
            }
            return new ControlEventMap(controlType, controlId);
        }

        private int MapControlToLedControlNumber(MidiControlType controlType, int controlId)
        {
            switch (controlType)
            {
                case MidiControlType.Encoder:
                    return controlId + 48;
                default:
                    return 0;
            }
        }
    }

    class ControlEventMap
    {
        public ControlEventMap(MidiControlType midiControlType, int controlId)
        {
            MidiControlType = midiControlType;
            ControlId = controlId;
        }

        public MidiControlType MidiControlType { get; }
        public int ControlId { get; }
    }

    class MidiControlAdaptor
    {
        public MidiControlAdaptor(MidiControlType controlType, int controlId, UnitType unitType, SimControlAdaptor simAdaptor)
        {
            ControlType = controlType;
            ControlId = controlId;
            UnitType = unitType;
            SimAdaptor = simAdaptor;
            SimAdaptor.ValueChanges.Subscribe(p => HandleValueChange(p));
            // TODO: Make sure the lights are initialised
        }

        public void HandleEvent(MidiEvent evt)
        {
            Console.WriteLine($"Got event: {evt.EventType}");
            switch (evt.EventType)
            {
                case MidiEventType.PitchBend:
                    var pitchBend = (PitchBendEvent)evt;
                    var value = MidiUnitConverter.ConvertFromPitch(UnitType, pitchBend.PitchValue);
                    Console.WriteLine($"Pitch bend value: {pitchBend.PitchValue} -> {value}");
                    SimAdaptor.TransmitValue(value);
                    break;
                case MidiEventType.ControlChange:
                    // TODO: How to control the sensitivity?
                    var cc = (ControlChangeEvent)evt;
                    var sensitivity = encoderSensitivity[UnitType];
                    var ccValue = SimAdaptor.Value + MidiUnitConverter.ConvertFromControlChange(UnitType, cc.ControlValue) * sensitivity;
                    Console.WriteLine($"ccValue: {ccValue}");
                    SimAdaptor.TransmitValue(ccValue);
                    break;
            }

        }

        public MidiControlType ControlType { get; }
        public int ControlId { get; }
        public UnitType UnitType { get; }
        public SimControlAdaptor SimAdaptor { get; }
        private Subject<LightControlMessage> lightControl = new Subject<LightControlMessage>();
        public IObservable<LightControlMessage> LightControl { get { return lightControl; } }

        private void HandleValueChange(double value)
        {
            var controlValue = MidiLed.CreateLedMessage(value, UnitType);
            lightControl.OnNext(new LightControlMessage(this, controlValue));
        }

        public bool MatchesEvent(ControlEventMap eventMap)
        {
            return ControlType == eventMap.MidiControlType && ControlId == eventMap.ControlId;
        }

        Dictionary<UnitType, double> encoderSensitivity = new Dictionary<UnitType, double>
        {
            { UnitType.Radians, 0.005 },
            { UnitType.Degrees, 1 },
            { UnitType.Percent, 0.01 }
        };
    }

    class MidiUnitConverter
    {
        public static double ConvertFromPitch(UnitType unitType, int value)
        {
            switch (unitType)
            {
                case UnitType.Percent:
                    return value / 16256.0;
                default:
                    return double.NaN;
            }
        }

        public static double ConvertFromControlChange(UnitType unitType, int value)
        {
            if (value <= 64)
            {
                return value;
            }
            else
            {
                return -(value - 64);
            }
        }
    }

    class MidiLed
    {
        public static uint CreateLedMessage(double value, UnitType unitType)
        {
            switch (unitType)
            {
                case UnitType.Radians:
                    return Convert.ToUInt32(22 + value * 5);
                case UnitType.Percent:
                    return Convert.ToUInt32(33 + value * 10);
                case UnitType.Degrees:
                    if (value >= 0 && value < 135 - 22.5 / 2 || value >= 360 - 22.5 / 2)
                    {
                        return Convert.ToUInt32(6 + value / 22.5);
                    }
                    if (value > 270 - 1.5 * 22.5 && value < 360 - 22.5 / 2)
                    {
                        var tmp = 1 + (value - (270 - 22.5)) / 22.5;
                        return Convert.ToUInt32(tmp);
                    }
                    return 0;
                default:
                    return 0;
            }
        }
    }

    class LightControlMessage
    {
        public LightControlMessage(MidiControlAdaptor adaptor, uint controlValue)
        {
            Adaptor = adaptor;
            ControlValue = controlValue;
        }

        public MidiControlAdaptor Adaptor { get; }
        public uint ControlValue { get; }
    }
}
