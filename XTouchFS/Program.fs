module MidiTesting.Program

open System
open MidiTesting.Util
open MidiTesting.Midi
open MidiTesting.FsInterface


// TODO: Constrain values on lights
// TODO: Lights for trim could show a more narrow range
// TODO: Initialise values?
// TODO: Add modes
// TODO: What about buttons that don't toggle?
// TODO: Go through all the other commands and see what might break this


type ControlConfig = { Control : XTouchControl; Field : FsField };;

let controlConfig = [
    { Control = Fader; Field = FsField.Throttle }
    { Control = Encoder 0; Field = FsField.HeadingBug }
    { Control = Encoder 1; Field = FsField.AltitudeBug }
    { Control = Encoder 2; Field = FsField.AutopilotVerticalSpeed }
    { Control = Encoder 7; Field = FsField.ElevatorTrim }
    { Control = Encoder 6; Field = FsField.Mixture }
    { Control = Encoder 5; Field = FsField.PropPitch }
    { Control = Button 8; Field = FsField.AutopilotEnabled }
    { Control = Button 9; Field = FsField.AutopilotAprEnabled }
    { Control = Button 10; Field = FsField.AutopilotNavEnabled }
    { Control = Button 11; Field = FsField.AutopilotFlightDirectorEnabled }
    { Control = Button 16; Field = FsField.AutopilotHdgEnabled }
    { Control = Button 17; Field = FsField.AutopilotAltEnabled }
    { Control = Button 18; Field = FsField.AutopilotVsEnabled }
//    | AutopilotIasEnabled
//    | AutopilotVnvEnabled
]

let fieldTypeSensitivity = Map [
    Percent, 0.01
    Radians, 0.005
    Degrees, 1.0
    Feet, 100.0
    FeetPerMinute, 100.0
    Boolean, 1.0
]

let calculateNewState (evt : XTouchEvent) (currentValue : double) (fieldType : FieldType)=
    match evt with
    | FaderEvent v -> Some v
    | EncoderEvent (_, v) -> Some <| currentValue + double v * fieldTypeSensitivity.Item fieldType
    | ButtonEvent (_, v) -> if v then Some ((currentValue + 1.0) % 2.0) else None
    
    
let handler (fs : FsConnection) (evt : XTouchEvent) =
    let control = evt.ToControl()
    let config = controlConfig |> Seq.tryFind (fun c -> c.Control = control)
    match config with
        | Some c -> maybe {
                let currentValue = fs.GetValue(c.Field)
                let! config = fieldMap.TryFind c.Field
                let fieldType = config.Type
                let! newValue = calculateNewState evt currentValue fieldType
                printfn "Updating %A: %f -> %f" c.Field currentValue newValue
                fs.SetValue(c.Field, newValue)
                return None
            }
        | None -> None
        |> ignore
    printfn "Midi event: %A" evt
    
    
let lightEncoderHandler (id : int, value : double, fieldType : FieldType) =
    maybe {
        let! (mode, lightValue) = match fieldType with
            | Radians -> Some <| (Pan, value)
            | Percent -> Some <| (Fan, value * 11.0)
            | Feet -> Some <| (Fan, value * 11.0 / 45000.0)
            | FeetPerMinute -> Some <| (Pan, value / 400.0)
            | Degrees ->
                let v =
                    if value >= 0.0 && value < 135.0 - 22.5 / 2.0 || value >= 360.0 - 22.5 / 2.0 then 5.0 + value / 22.5
                    elif value > 270.0 - 1.5 * 22.5 && value < 360.0 - 22.5 / 2.0 then (value - (270.0 - 22.5)) / 22.5
                    else -1.0
                Some <| (Single, v)
            | _ -> None
        return EncoderLight(id = id, Mode = mode, Value = int (Math.Round(lightValue, 0)))
    }
    
    
let lightButtonHandler (id : int, value : double) =
    let state = if value = 1.0 then ButtonState.On else ButtonState.Off
    Some <| ButtonLight(id, state)
    
    
let updateLights (midi : MidiInterface) (field : FsField) (value : double) =
    maybe {
        let! config = controlConfig |> Seq.tryFind (fun c -> c.Field = field)
        let! fieldConfig = fieldMap.TryFind field
        let! msg = match config.Control with
            | Fader -> None
            | Encoder i -> lightEncoderHandler(i, value, fieldConfig.Type)
            | Button i -> lightButtonHandler(i, value)
        midi.UpdateLight msg
        return ()
    } |> ignore
    
    
let lightsHandler (midi : MidiInterface) (evt : FieldChange) =
    dprintfn "Got light event: %A" evt
    updateLights midi evt.Field evt.Value
    
    
let initialiseFields (midi : MidiInterface) (fs : FsConnection) =
    controlConfig |> Seq.iter (fun c -> fs.ActivateField c.Field)
    controlConfig |> Seq.iter (fun c -> fs.GetValue(c.Field) |> updateLights midi c.Field)


let run (fs : FsConnection) = 
    printfn "Loading midi interface"
    let midi = MidiInterface()
//    let fs = FsInterface()
    let callback = handler fs
    let lightsCallback = lightsHandler midi
    midi.Events
        |> Observable.subscribe callback
        |> ignore
    fs.FieldChanges
        |> Observable.subscribe lightsCallback
        |> ignore
    initialiseFields midi fs
    {| Midi = midi; Fs = fs; LightsCallback = lightsCallback; Callback = callback |}
    
    
//[<EntryPoint>]
//let main argv =
//    printfn "Loading midi interface"
//    let midi = MidiInterface()
//    let fs = FsInterface()
//    let callback = handler fs
//    let lightsCallback = lightsHandler midi fs
//    midi.Events
//        |> Observable.subscribe callback
//        |> ignore
//    fs.FieldChanges
//        |> Observable.subscribe lightsCallback
//        |> ignore
//    Console.ReadLine() |> ignore
//    0