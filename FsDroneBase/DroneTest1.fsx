#load "SetUpEnv.fsx"

open FsDrone

let cntlr = new DroneController()
let sub1 = cntlr.Monitor.Subscribe(fun msg -> printfn "%A" msg)
let sub2 = cntlr.Monitor.Subscribe(function ConnectionState (Connected tm) -> )
Observable.