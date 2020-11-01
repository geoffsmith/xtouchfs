module MidiTesting.Util

type MaybeBuilder() =

    member this.Bind(x, f) = 
        match x with
        | None -> None
        | Some a -> f a

    member this.Return(x) = 
        Some x
        
    member this.Zero() =
        ()
   
let maybe = new MaybeBuilder()


let inline dprintfn fmt =
    Printf.ksprintf System.Diagnostics.Debug.WriteLine fmt
