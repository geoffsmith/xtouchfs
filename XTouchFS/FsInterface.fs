module public MidiTesting.FsInterface

open System
open MidiTesting.Util

type FsField =
    | Throttle
    | ElevatorTrim
    | Mixture
    | PropPitch
    | HeadingBug
    | AltitudeBug
    | AutopilotEnabled
    
    
type FieldType =
    | Radians
    | Percent
    | Degrees
    | Feet
    | Boolean
    | Other
    
    
type FieldRange = { Min : double option; Max : double option; Wraps : bool }

let defaultRanges = Map [
    Radians, {Min = Some -1.0; Max = Some 1.0; Wraps = false}
    Percent, {Min = Some 0.0; Max = Some 1.0; Wraps = false}
    Degrees, {Min = Some 0.0; Max = Some 360.0; Wraps = true}
    Boolean, {Min = Some 0.0; Max = Some 1.0; Wraps = true}
    Feet, {Min = Some 0.0; Max = None; Wraps = false}
]

type FieldConfig =
    {
        Field : FsField
        Variable : string
        Event : string
        Type : FieldType
        Range : FieldRange option
    }
    static member Define(field : FsField, variable : string, event : string, fieldType : FieldType,
                         ?range : FieldRange) =
        let range' = match range with
                     | Some x -> Some x
                     | None -> defaultRanges.TryFind fieldType
        { Field = field; Variable = variable; Event = event; Type = fieldType; Range = range' }
    
type FieldChange = { Field : FsField; Value : double }

// TODO: This should be move to the wrapper and made available through the interface
let fields = [
    FieldConfig.Define(Throttle, "GENERAL ENG THROTTLE LEVER POSITION:1", "THROTTLE_SET", Percent)
    FieldConfig.Define(ElevatorTrim, "ELEVATOR TRIM PCT", "ELEVATOR_TRIM_SET", Radians)
    FieldConfig.Define(Mixture, "GENERAL ENG MIXTURE LEVER POSITION:1", "MIXTURE_SET", Percent)
    FieldConfig.Define(PropPitch, "GENERAL ENG PROPELLER LEVER POSITION:1", "PROP_PITCH_SET", Percent)
    FieldConfig.Define(HeadingBug, "AUTOPILOT HEADING LOCK DIR", "HEADING_BUG_SET", Degrees)
    FieldConfig.Define(AltitudeBug, "AUTOPILOT ALTITUDE LOCK VAR", "AP_ALT_VAR_SET_ENGLISH", Feet)
    FieldConfig.Define(AutopilotEnabled, "AUTOPILOT MASTER", "AP_MASTER", Boolean)
]

let fieldMap = fields |> Seq.map (fun f -> f.Field, f) |> Map.ofSeq

type FsConnection =
    abstract member ActivateField : FsField -> unit
    abstract member GetValue : FsField -> double
    abstract member SetValue : FsField * double -> unit
    abstract member FieldChanges : IEvent<FieldChange>
    

type FsInterface() =
    let mutable values : Map<FsField, double> = Map.empty
    let events = Event<FieldChange>()
    
    member x.GetValue(field : FsField) =
        match Map.tryFind field values with
        | Some value -> value
        | None -> 0.0
        
    member x.GetFieldConfig(field : FsField) = fieldMap.Item field
    
    member x.SetValue(field : FsField, value : double) =
        maybe {
            let! config = fieldMap.TryFind field
            let clippedValue = match config.Range with
                | Some({Min = Some min; Max = Some max; Wraps = true}) when value < min -> max
                | Some({Min = Some min; Max = Some max; Wraps = true}) when value > max -> min
                | Some({Min = Some min}) when value < min -> min
                | Some({Max = Some max}) when value > max -> max
                | _ -> value
            values <- Map.add field clippedValue values
            events.Trigger({Field = field; Value = clippedValue})
            return None
        } |> ignore
        
    member x.FieldChanges = events.Publish
    
    
let prepareValueTransmit (field : FsField) (value : double) =
    maybe {
        let! config = fieldMap.TryFind field
        return match config.Range with
            | Some({Min = Some min; Max = Some max; Wraps = true}) when value < min -> max
            | Some({Min = Some min; Max = Some max; Wraps = true}) when value > max -> min
            | Some({Min = Some min}) when value < min -> min
            | Some({Max = Some max}) when value > max -> max
            | _ -> value
    }
