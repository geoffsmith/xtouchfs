using Microsoft.FlightSimulator.SimConnect;
using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Reflection;
using System.Runtime.InteropServices;

// TODO: Handle multiple throttles
namespace FSKontrol.WPF
{
    class FsConnection
    {
        const int WM_USER_SIMCONNECT = 0x0402;
        SimConnect simconnect = null;
        List<SimControlAdaptor> controlAdaptors = new List<SimControlAdaptor>();

        public double GetValue(Field field)
        {
            return 0.0;
        }

        public void SetValue(Field field, double val)
        {
        }

        public IObservable<double> Subscribe(Field field)
        {
            return null;
        }
        
        public FsConnection(IntPtr windowHandle)
        {
            try
            {
                simconnect = new SimConnect("Managed Data Request", windowHandle, WM_USER_SIMCONNECT, null, 0);
                simconnect.OnRecvOpen += new SimConnect.RecvOpenEventHandler(simconnect_OnRecvOpen);
                simconnect.OnRecvQuit += new SimConnect.RecvQuitEventHandler(simconnect_OnRecvQuit);
                simconnect.OnRecvException += new SimConnect.RecvExceptionEventHandler(simconnect_OnRecvException);
            }
            catch (COMException ex)
            {
                Console.WriteLine("Got exception" + ex.Message);
            }
        }

        public void ReceiveMessage()
        {
            simconnect.ReceiveMessage();
        }

        void simconnect_OnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
            Console.WriteLine("Connected to sim");
            this.InitDataRequest();
        }

        void simconnect_OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data)
        {
            Console.WriteLine("Sim has exited");
        }

        void simconnect_OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
        {
            Console.WriteLine("Exception received: " + data.dwException);
        }

        private void InitDataRequest()
        {
            try
            {
                //RegisterDefinitions();
                simconnect.OnRecvSimobjectDataBytype += new SimConnect.RecvSimobjectDataBytypeEventHandler(simconnect_OnRecvSimobjectDataBytype);
                simconnect.OnRecvEvent += new SimConnect.RecvEventEventHandler(simconnect_OnRecvEvent);
                //simconnect.RequestDataOnSimObjectType(Field.Throttle, Field.Throttle, 0, SIMCONNECT_SIMOBJECT_TYPE.USER);
                //simconnect.MapClientEventToSimEvent(EVENTS.SetHeadingBug, "HEADING_BUG_SET");
                //simconnect.MapClientEventToSimEvent(EVENTS.SetAPPanelAltitude, "AP_ALT_VAR_SET_ENGLISH");
                //simconnect.MapClientEventToSimEvent(EVENTS.G1000MFDZoomIn, "G1000_MFD_ZOOMIN_BUTTON");
                //simconnect.MapClientEventToSimEvent(EVENTS.SetElevatorTrim, "ELEVATOR_TRIM_SET");
                //simconnect.AddClientEventToNotificationGroup(GROUPS.GroupA, EVENTS.SetElevatorTrim, false);
                simconnect.SetNotificationGroupPriority(GROUPS.GroupA, SimConnect.SIMCONNECT_GROUP_PRIORITY_HIGHEST);
            }
            catch (COMException ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public SimControlAdaptor CreateAdaptor(Field definition)
        {
            FieldInfo fieldInfo = typeof(Field).GetField(definition.ToString());
            DataDefinition[] attribs = fieldInfo.GetCustomAttributes(typeof(DataDefinition), false) as DataDefinition[];
            if (attribs.Length == 0) return null;
            var attrib = attribs[0];
            // Set up data definition
            Console.WriteLine($"Def {attrib.DatumName}, {attrib.UnitType}");
            var unitConfig = UnitTypeConfig.GetConfig(attrib.UnitType);
            simconnect.AddToDataDefinition(definition, attrib.DatumName, unitConfig.Units, 
                unitConfig.DataType, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            var method = typeof(SimConnect).GetMethod("RegisterDataDefineStruct");
            var methodRef = method.MakeGenericMethod(unitConfig.Struct);
            methodRef.Invoke(simconnect, new object[] { definition });

            // Get initial value
            simconnect.RequestDataOnSimObjectType(definition, definition, 0, SIMCONNECT_SIMOBJECT_TYPE.USER);

            // Create event mapping
            simconnect.MapClientEventToSimEvent(definition, attrib.EventId);
            simconnect.AddClientEventToNotificationGroup(GROUPS.GroupA, definition, false);

            var controlAdaptor = new SimControlAdaptor(definition, attrib.UnitType, unitConfig.MinValue, unitConfig.MaxValue, 
                simconnect);
            controlAdaptors.Add(controlAdaptor);
            return controlAdaptor;
        }

        void simconnect_OnRecvSimobjectDataBytype(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA_BYTYPE data)
        {
            Field definition = (Field)data.dwRequestID;
            foreach (var controlAdaptor in controlAdaptors)
            {
                if (definition != controlAdaptor.Definition) continue;
                DoubleStruct s2 = (DoubleStruct)data.dwData[0];
                controlAdaptor.ReceiveObjectData(s2.value);
            }
        }

        void simconnect_OnRecvEvent(SimConnect sender, SIMCONNECT_RECV_EVENT recEvent)
        {
            Field definition = (Field)recEvent.uEventID;
            Console.WriteLine($"Received event: {definition}");
            foreach (var controlAdaptor in controlAdaptors)
            {
                if (definition != controlAdaptor.Definition) continue;
                controlAdaptor.ReceiveEvent(recEvent.dwData);
            }
        }
    }

    enum Field
    {
        [DataDefinition("GENERAL ENG THROTTLE LEVER POSITION:1", "THROTTLE_SET", UnitType.Percent)]
        Throttle,
        [DataDefinition("ELEVATOR TRIM PCT", "ELEVATOR_TRIM_SET", UnitType.Radians)]
        ElevatorTrim,
        [DataDefinition("GENERAL ENG MIXTURE LEVER POSITION:1", "MIXTURE_SET", UnitType.Percent)]
        Mixture,
        [DataDefinition("GENERAL ENG PROPELLER LEVER POSITION:1", "PROP_PITCH_SET", UnitType.Percent)]
        PropellerPitch,
        [DataDefinition("AUTOPILOT HEADING LOCK DIR", "HEADING_BUG_SET", UnitType.Degrees)]
        AutopilotHeadingBug,
        AutopilotAltitudeBug
    }

    enum EVENTS
    {
        SetHeadingBug,
        SetAPPanelAltitude,
        G1000MFDZoomIn,
        SetElevatorTrim
    }

    enum GROUPS
    {
        GroupA
    }

    enum DATA_REQUESTS
    {
        REQUEST_1,
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    struct DoubleStruct
    {
        public double value;
    };

    [AttributeUsage(AttributeTargets.All)]
    class DataDefinition : Attribute
    {
        public string DatumName { get; }
        public string EventId { get; }
        public UnitType UnitType { get; }

        public DataDefinition(string datumName, string eventId, UnitType unitType)
        {
            DatumName = datumName;
            EventId = eventId;
            UnitType = unitType;
        }
    }

    enum UnitType
    {
        Percent,
        Degrees,
        Feet,
        Radians
    }

    class UnitTypeConfig
    {
        public string Units { get; set; }
        public SIMCONNECT_DATATYPE DataType { get; set; }
        public Type Struct { get; set; }
        public double MinValue { get; set; }
        public double MaxValue { get; set; }

        public static UnitTypeConfig GetConfig(UnitType unitType)
        {
            if (unitType == UnitType.Percent)
            {
                return new UnitTypeConfig
                {
                    Units = "percent",
                    DataType = SIMCONNECT_DATATYPE.FLOAT64,
                    Struct = typeof(DoubleStruct),
                    MinValue = 0,
                    MaxValue = 1
                };
            }
            if (unitType == UnitType.Radians)
            {
                return new UnitTypeConfig
                {
                    Units = "radians",
                    DataType = SIMCONNECT_DATATYPE.FLOAT64,
                    Struct = typeof(DoubleStruct),
                    MinValue = -1,
                    MaxValue = 1
                };
            }
            if (unitType == UnitType.Degrees)
            {
                return new UnitTypeConfig
                {
                    Units = "degrees",
                    DataType = SIMCONNECT_DATATYPE.FLOAT64,
                    Struct = typeof(DoubleStruct),
                    MinValue = 0,
                    MaxValue = 360
                };
            }
            return null;
        }
    }

    class SimControlAdaptor
    {
        public SimControlAdaptor(Field definition, UnitType unitType, double minValue, double maxValue, SimConnect simConnect)
        {
            Definition = definition;
            UnitType = unitType;
            MinValue = minValue;
            MaxValue = maxValue;
            SimConnect = simConnect;
        }

        public double Value {
            get => val; 
            set {
                val = value;
                valueChanges.OnNext(val);
            }
        }

        public Field Definition { get; }
        public UnitType UnitType { get; }
        public double MinValue { get; }
        public double MaxValue { get; }
        public SimConnect SimConnect { get; }
        public IObservable<double> ValueChanges { get { return valueChanges; } }

        private Subject<double> valueChanges = new Subject<double>();
        private double val;

        public void TransmitValue(double newValue)
        {
            newValue = ClipValue(newValue);
            var transmitVal = UnitConverter.ConvertToEvent(UnitType, newValue);
            SimConnect.TransmitClientEvent(SimConnect.SIMCONNECT_OBJECT_ID_USER, 
                Definition,
                transmitVal,
                GROUPS.GroupA,
                SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
        }

        public void ReceiveEvent(uint data)
        {
            Value = UnitConverter.ConvertFromEvent(UnitType, data);
            Console.WriteLine($"New value after event: {Value}");
        }

        public void ReceiveObjectData(double value)
        {
            Value = UnitConverter.ConvertFromObjectData(UnitType, value);
            Console.WriteLine($"Received object data {Definition}: {Value}");
        }

        double ClipValue(double newValue)
        {
            if (UnitType == UnitType.Degrees)
            {
                if (newValue < 0) return newValue % 360 + 360;
                return newValue % 360;
            }
            return Math.Min(Math.Max(MinValue, newValue), MaxValue);
        }
    }

    class UnitConverter
    {
        public static double ConvertFromEvent(UnitType unitType, uint data)
        {
            switch (unitType)
            {
                case UnitType.Percent:
                    return data / 16384.0;
                case UnitType.Degrees:
                    return data;
                case UnitType.Radians:
                    var convertedData = ((int)data) / 16384.0;
                    return convertedData;
                default:
                    Console.WriteLine($"Unhandled event data convert: {unitType} {data}");
                    return double.NaN;
            }
        }

        public static uint ConvertToEvent(UnitType unitType, double value)
        {
            switch (unitType)
            {
                case UnitType.Percent:
                    return Convert.ToUInt32(value * 16384);
                case UnitType.Radians:
                    return (uint)(value * 16384);
                case UnitType.Degrees:
                    return Convert.ToUInt32(value);
                default:
                    return 0;
            }
        }

        public static double ConvertFromObjectData(UnitType unitType, double value)
        {
            switch (unitType)
            {
                case UnitType.Percent:
                    return value / 100.0;
                case UnitType.Radians:
                    return value;
                case UnitType.Degrees:
                    return value;
                default:
                    Console.WriteLine($"Unhandled object data convert: {unitType} {value}");
                    return double.NaN;
            }
        }

    }


}
