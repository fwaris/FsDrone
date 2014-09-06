#load "SetUpEnv.fsx"

open FsDrone
open System
open System.Threading

let cntlr = new DroneController()
let stop() = (cntlr :> IDisposable).Dispose()
(*
stop()
*)

let sub1 = cntlr.Monitor.Subscribe (printfn "%A")
let sub3 = cntlr.ConfigObs.Subscribe (printfn "%A")

let prevDroneState = ref (enum<ArdroneState>(0))
let showTelemetry = function
    | DroneState ds when ds = !prevDroneState -> ()
    | DroneState ds -> prevDroneState := ds; printfn "%A" ds
    | telemetry -> printfn "%A" telemetry
//
let sub2 = cntlr.Telemetry.Subscribe showTelemetry

let connectionCts = new CancellationTokenSource()
Async.Start(cntlr.ConnectAsync(connectionCts))

cntlr.Send GetConfig

cntlr.Emergency()

