module MidiTesting.Midi

open Commons.Music.Midi
open MidiTesting.Util

type EncoderLightMode =
    | Pan
    | Fan
    | Spread
    | Single
    
type ButtonState =
    | On
    | Off
        
type LightState =
    | ButtonLight of id : int * State : ButtonState
    | EncoderLight of id : int * Mode : EncoderLightMode * Value : int
    
type XTouchControl =
    | Button of int
    | Encoder of int
    | Fader

type XTouchEvent =
    | FaderEvent of double
    | ButtonEvent of int * bool
    | EncoderEvent of int * int
    
    member x.ToControl() =
        match x with
        | FaderEvent (_) -> Fader
        | ButtonEvent (id, _) -> Button id
        | EncoderEvent (id, _) -> Encoder id
    
    
let buttonMap = Map [
    // Bottom row
    87, 0
    88, 1
    91, 2
    92, 3
    86, 4
    93, 5
    94, 6
    95, 7
    // Top row
    89, 8
    90, 9
    40, 10
    41, 11
    42, 12
    43, 13
    44, 14
    45, 15
    // Encoder buttons
    32, 16
    33, 17
    34, 18
    35, 19
    36, 20
    37, 21
    38, 22
    39, 23
    // Layer buttons
    85, 24
    84, 25
]

let buttonInverseMap = buttonMap |> Map.toList |> Seq.map (fun (a, b) -> (b, a)) |> Map

type MidiDirection =
    | In
    | Out

let rec tryOpenMidiInput (name : string, direction : MidiDirection) = async {
    let access = MidiAccessManager.Default
    let devices = match direction with
        | In -> access.Inputs
        | Out -> access.Outputs
    let input = devices |> Seq.tryFind (fun i -> i.Name = name)
    match input with
        | Some i -> return i
        | None ->
            printfn "Could not find midi device: %s" name
            do! Async.Sleep(1000)
            return! tryOpenMidiInput(name, direction)
}
    
let openMidiInput (name : string) =
    async {
        let! input = tryOpenMidiInput(name, MidiDirection.In)
        let access = MidiAccessManager.Default
        return! access.OpenInputAsync(input.Id) |> Async.AwaitTask
    }
    
let openMidiOutput (name : string) =
    async {
        let! input = tryOpenMidiInput(name, MidiDirection.Out)
        let access = MidiAccessManager.Default
        return! access.OpenOutputAsync(input.Id) |> Async.AwaitTask
    }
    
    
let receiveEncoderMessage (midiControlId : byte, midiControlValue : byte) =
    let controlId = int midiControlId - 16
    let midiValue = int midiControlValue
    let value = if midiValue <= 64 then midiValue else -(midiValue - 64)
    EncoderEvent(controlId, value)
    
    
let receiveButtonMessage (midiControlId: byte, midiControlValue : byte) =
    let controlId = buttonMap.TryFind(int midiControlId)
    let on = if int midiControlValue = 0 then false else true
    match controlId with
    | Some id -> ButtonEvent(id, on) |> Some
    | None -> None
    
let receiveFaderMessage (midiControlValue : byte) =
    FaderEvent(float (int midiControlValue) / 127.0)
    
let receiveMidiMessage (msg : MidiReceivedEventArgs) =
    match msg.Data |> Array.toList with
    | 176uy :: controlId :: value :: _ -> receiveEncoderMessage(controlId, value) |> Some
    | 144uy :: controlId :: value :: _ -> receiveButtonMessage(controlId, value)
    | 232uy :: _ :: value :: _ -> receiveFaderMessage(value) |> Some
    | _ ->
        printfn "Received unhandled message: %A" msg.Data
        None
        
let encoderLightMidiMessage (id : int, mode : EncoderLightMode, value : int) =
    // 1 -> 11: Single
    // 17 -> 27: Pan
    // 33 -> 43: Fan
    // 49 -> 54: Spread
    let controlId = id + 48
    let controlValue = match mode with
                       | Single -> 1 + value
                       | Pan -> 22 + value
                       | Fan -> 33 + value
                       | Spread -> 49 + value
    [|176uy; byte controlId; byte controlValue|]
    
    
let buttonLightMidiMessage (id : int, state : ButtonState) =
    maybe {
        let! controlId = buttonInverseMap.TryFind id
        let controlValue = match state with
                           | On -> 127.0
                           | Off -> 0.0
        return [|144uy; byte controlId; byte controlValue|]
    }
    
type MidiInterface () =
    let events = Event<XTouchEvent>()
    let deviceName = "X-TOUCH MINI"
    let mutable midiOutput = None
    let mutable midiInput = None
    
    do
        async {
            let! input = openMidiInput(deviceName)
            midiInput <- Some input
            input.MessageReceived
                |> Observable.choose receiveMidiMessage
                |> Observable.subscribe events.Trigger
                |> ignore
            let! output = openMidiOutput(deviceName)
            midiOutput <- Some output
            match midiOutput with
                  | Some output -> 
                    dprintfn "Sending MC mode init"
                    output.Send([|176uy; 127uy; 1uy;|], 0, 3, 0L)
                  | None -> ()
            ()
        } |> Async.Start
        
    member x.UpdateLight(state : LightState) =
        maybe {
            let! msg = match state with
                       | EncoderLight (id, mode, value) -> encoderLightMidiMessage(id, mode, value) |> Some
                       | ButtonLight (id, value) -> buttonLightMidiMessage(id, value)
                       | _ -> None
            let! o = midiOutput
            o.Send(msg, 0, 3, 0L)
            return ()
        } |> ignore
        
        
    member x.Events = events.Publish
