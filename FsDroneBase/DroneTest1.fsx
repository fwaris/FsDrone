#load "SetUpEnv.fsx"

open FsDrone
open System
open System.Threading

let cntlr = new DroneController()
let stop() = (cntlr :> IDisposable).Dispose()
(*
stop()
*)

let sub1 = cntlr.Monitor.Subscribe(fun msg -> printfn "%A" msg)
let sub2 = cntlr.Telemetry.Subscribe(fun msg -> printfn "%A" msg)
let connectionCts = new CancellationTokenSource()
Async.Start(cntlr.ConnectAsync(connectionCts))

cntlr.Emergency()

let isLanded = function Landed      -> true | _ -> false
let isFlying = function Flying _    -> true | _ -> false

let takeoff = {Name="Takeoff"; Commands=WhenIn_RepeatTill (isLanded, Takeoff, isFlying)}
