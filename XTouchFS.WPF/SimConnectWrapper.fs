module XTouchFS.SimConnect

open System
open Microsoft.FSharp.Reflection
open Microsoft.FlightSimulator.SimConnect
open MidiTesting.FsInterface
open MidiTesting.Util

let WM_USER_SIMCONNECT = uint32 0x0402

let fieldIndices = (FSharpType.GetUnionCases typeof<FsField>)
                   |> Seq.mapi (fun i f -> ((FSharpValue.MakeUnion(f, [| |]) :?> FsField), i))
                   |> Map
let reverseFieldIndices = fieldIndices |> Map.toSeq |>  Seq.map (fun (a, b) -> b, a) |> Map

type Groups =
    GroupA = 0
    
    
let fieldTypeToUnit = function
    | Radians -> "Radians"
    | Percent -> "Percent over 100"
    | Degrees -> "Degrees"
    | Feet -> "Feet"
    | FeetPerMinute -> "Feet/minute"
    | Boolean -> "Bool"
    | Mhz -> "MHz"
    | FrequencyBcd16 -> "Frequency BCD16"
    | Other -> ""
    
let fieldTypeToDataType = function
    | Radians -> SIMCONNECT_DATATYPE.FLOAT64
    | Percent -> SIMCONNECT_DATATYPE.FLOAT64
    | Degrees -> SIMCONNECT_DATATYPE.FLOAT64
    | _ -> SIMCONNECT_DATATYPE.FLOAT64
    
type DoubleStruct =
    struct
        val value : double
    end
    
    
let toBcd16 (value: double) =
    let valueStr = sprintf "%07.3f" value
    let a = valueStr.Chars 1 |> string |> int
    let b = valueStr.Chars 2 |> string |> int
    let c = valueStr.Chars 4 |> string |> int
    let d = valueStr.Chars 5 |> string |> int
    let r = (a <<< 12) ||| (b <<< 8) ||| (c <<< 4) ||| d
    (uint32) r
    
    
let fromBcd16 (value: uint32) =
    let mask = uint32 0xf
    let d = value &&& mask
    let c = (value >>> 4) &&& mask
    let b = (value >>> 8) &&& mask
    let a = (value >>> 12) &&& mask
    sprintf "1%d%d.%d%d0" a b c d |> double
    
    
    
// TODO: Would it help to wrap the values in their type?
let eventValue (v : double) = function
    | Percent -> Convert.ToUInt32(v * 16384.0)
    | Radians -> uint32 (v * 16384.0)
    | Degrees -> Convert.ToUInt32(v)
    | Feet -> Convert.ToUInt32(v)
    | FeetPerMinute -> uint32 (v)
    | Mhz -> uint32 (v * 1000000.0)
    | _ -> 0u
        

type SimConnectWrapper (windowHandle : nativeint) as x =
    let mutable simConnect : SimConnect option = None
    let mutable requestedFields : Set<FsField> = Set.empty
    let mutable fieldValues : Map<FsField, double> = Map.empty
    let events = Event<FieldChange>()


        
    let onExceptionHandler (sender : SimConnect) (data : SIMCONNECT_RECV) =
        dprintfn "SimConnect exception: %A" data
        match data with
        | :? SIMCONNECT_RECV_EXCEPTION as ex ->
            let exType = enum<SIMCONNECT_EXCEPTION>(int32 ex.dwException)
            dprintfn "Exception details: %A %A %A %A %A" ex.dwException exType ex.dwIndex ex.dwID ex.dwSendID
        | _ -> ()
        ()
        
    let onSimObjectDataHandler (sender : SimConnect) (data : SIMCONNECT_RECV) =
        let evt = data :?> SIMCONNECT_RECV_SIMOBJECT_DATA
        maybe {
            let! field = reverseFieldIndices.TryFind (int evt.dwRequestID)
            let! s = evt.dwData |> Array.tryHead
            let! result = match s with
                          | :? DoubleStruct as r -> Some r
                          | _ -> None
            let value = result.value
            dprintfn "Got sim object value: %A" value
            fieldValues <- Map.add field value fieldValues
            events.Trigger({Field = field; Value = value})
            return ()
        } |> ignore
        ()
        
//    let onEventHandler (sender : SimConnect) (data : SIMCONNECT_RECV) =
//        //dprintfn "Receive event"
//        ()


    do
        async {
            while true do
                if simConnect = None then
                    dprintfn "Attempting to reconnect to flight sim"
                    x.TryConnect()
            do! Async.Sleep(1000)
        } |> Async.Start
        
    member x.TryConnect () =
        try
            maybe {
                let c = new SimConnect("Managed Data Request", windowHandle, WM_USER_SIMCONNECT, null, 0u)
                simConnect <- Some c
                c.add_OnRecvOpen(SimConnect.RecvOpenEventHandler x.onOpenHandler)
                c.add_OnRecvQuit(SimConnect.RecvQuitEventHandler x.onQuitHandler)
                c.add_OnRecvException(SimConnect.RecvExceptionEventHandler onExceptionHandler)
                c.add_OnRecvSimobjectData(SimConnect.RecvSimobjectDataEventHandler onSimObjectDataHandler)
            } |> ignore
        with
            | _ as e ->
                dprintfn "Failed to connect: %A" e
        
    member x.onOpenHandler (sender : SimConnect) (data : SIMCONNECT_RECV_OPEN) =
        printfn "Simconnect connected"
        requestedFields |> Set.map (fun f -> x.SetupField(f)) |> ignore
        ()
        
    member x.onQuitHandler (sender : SimConnect) (data : SIMCONNECT_RECV) =
        dprintfn "SimConnect quit"
        simConnect <- None
        ()

    member x.ReceiveMessage() =
        try
            maybe {
                let! s = simConnect
                s.ReceiveMessage()
                return ()
            } |> ignore
        with
            | _ as e -> dprintfn "Exception receiving sim connect message %A" e
            
    member x.SetupField (field : FsField) =
        maybe {
            dprintfn "Setting up field %A" field
            requestedFields <- Set.add field requestedFields
            let! c = simConnect
            let! fieldIndex = fieldIndices.TryFind field
            let! config = fieldMap.TryFind field
            let fieldEnum = enum<Groups>(fieldIndex)
            c.MapClientEventToSimEvent(enum<Groups>(fieldIndex), config.Event)
            c.AddClientEventToNotificationGroup(Groups.GroupA, enum<Groups>(fieldIndex), false)
            let units = fieldTypeToUnit config.Type
            let dataType = fieldTypeToDataType config.Type
            c.AddToDataDefinition(fieldEnum, config.Variable, units, dataType, 0.0f, SimConnect.SIMCONNECT_UNUSED)
            c.RegisterDataDefineStruct<DoubleStruct>(fieldEnum)
            c.RequestDataOnSimObject(fieldEnum, fieldEnum, SimConnect.SIMCONNECT_OBJECT_ID_USER,
                                     SIMCONNECT_PERIOD.VISUAL_FRAME,
                                     SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0u, 0u, 0u)
            return ()
        } |> ignore
        
    
    interface FsConnection with
        member x.GetValue(field : FsField) =
            // TODO: Are there race conditions around this?
            if not (requestedFields.Contains field) then
                x.SetupField(field) |> ignore
            // TODO: Make this optional rather than always return
            // TODO: Perhaps wait for initial value?
            match fieldValues.TryFind field with
            | Some v -> v
            | None -> 0.0
            
        member x.SetValue(field : FsField, value : double) =
            dprintfn "Setting value %A: %A" field value
            maybe {
                let! c = simConnect
                let! config = fieldMap.TryFind field
                let! transmitValue = prepareValueTransmit field value
                let eValue = eventValue transmitValue config.Type
                let! fieldIndex = fieldIndices.TryFind field
                dprintfn "Transmit value: %f, Evalue: %A fieldIndex: %A" transmitValue eValue fieldIndex
                c.TransmitClientEvent(SimConnect.SIMCONNECT_OBJECT_ID_USER, 
                    enum<Groups>(fieldIndex),
                    eValue,
                    Groups.GroupA,
                    SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY)
                return ()
            } |> ignore
            
        member x.FieldChanges = events.Publish
        
        member x.ActivateField(field : FsField) =
            if not (requestedFields.Contains field) then
                x.SetupField field
