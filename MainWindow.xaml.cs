using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Devices;
using Microsoft.FlightSimulator.SimConnect;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FSKontrol.WPF
{


    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        IntPtr handle;
        HwndSource handleSource;
        FsConnection fsConnection = null;
        MidiConnection midiConnection = null;
        List<MidiSimControl> controls = new List<MidiSimControl>
        {
            new MidiSimControl(MidiControlType.Fader, 0, Field.Throttle),
            new MidiSimControl(MidiControlType.Encoder, 7, Field.ElevatorTrim),
            new MidiSimControl(MidiControlType.Encoder, 6, Field.Mixture),
            new MidiSimControl(MidiControlType.Encoder, 5, Field.PropellerPitch),
            new MidiSimControl(MidiControlType.Encoder, 0, Field.AutopilotHeadingBug),
            //new MidiSimControl(MidiControlType.Encoder, 1, Field.AutopilotAltitudeBug)
        };


        public MainWindow()
        {
            InitializeComponent();
            handle = new WindowInteropHelper(this).EnsureHandle(); // Get handle of main WPF Window
            handleSource = HwndSource.FromHwnd(handle); // Get source of handle in order to add event handlers to it
            handleSource.AddHook(HandleSimConnectEvents);
            fsConnection = new FsConnection(handle);
            midiConnection = new MidiConnection();
            SetupControls();

        }
        
        ~MainWindow()
        {
            if (handleSource != null)
            {
                handleSource.RemoveHook(HandleSimConnectEvents);
            }
        }

        private IntPtr HandleSimConnectEvents(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam, ref bool isHandled)
        {
            isHandled = false;

            switch (message)
            {
                case FsConnection.WM_USER_SIMCONNECT:
                    {
                        if (fsConnection != null)
                        {
                            fsConnection.ReceiveMessage();
                            isHandled = true;
                        }
                    }
                    break;

                default:
                    break;
            }

            return IntPtr.Zero;
        }



        private void SetupControls()
        {
            foreach (var control in controls)
            {
                var simAdaptor = fsConnection.CreateAdaptor(control.Definition);
                if (simAdaptor is null) continue;
                var midiAdaptor = midiConnection.CreateAdaptor(control.ControlType, control.ControlId, simAdaptor);

            }
        }
    }
}
