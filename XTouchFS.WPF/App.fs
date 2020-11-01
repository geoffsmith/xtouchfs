namespace XTouchFS.WPF

open System

open MidiTesting.FsInterface
open Xamarin.Forms
open Xamarin.Forms.Platform.WPF
open System.Diagnostics
open System.Windows.Interop
open XTouchFS.SimConnect

type MainWindow() = 
    inherit FormsApplicationPage()

module Main = 
    let handleSimConnectEvents (wrapper : SimConnectWrapper) (hWnd : IntPtr) (message : int) (wParam : IntPtr) (lParam : IntPtr) (isHandled : byref<bool>) =
        isHandled <- false
        if message = int WM_USER_SIMCONNECT then
            wrapper.ReceiveMessage()
            isHandled <- true
        IntPtr.Zero


    [<EntryPoint>]
    [<STAThread>]
    let main(_args) =
        printfn "Loading the app"
        Console.WriteLine("Console.WriteLine")
        Debug.WriteLine("Debug.WriteLine")

        let app = new System.Windows.Application()
        Forms.Init()
        let window = MainWindow() 
        let handle = WindowInteropHelper(window).EnsureHandle() // Get handle of main WPF Window
        let handleSource = HwndSource.FromHwnd(handle) // Get source of handle in order to add event handlers to it
        let wrapper = SimConnectWrapper(handle)
        handleSource.AddHook(HwndSourceHook (fun a b c d e -> handleSimConnectEvents wrapper a b c d &e))
        (wrapper :> FsConnection).GetValue(Throttle) |> ignore

        window.LoadApplication(new XTouchFS.App(wrapper))

        app.Run(window)
