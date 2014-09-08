#load "SetUpEnv.fsx"

open FsDrone
open System
open System.Threading

let cntlr = new DroneController()
let stop() = (cntlr :> IDisposable).Dispose()

let sub1 = cntlr.Monitor.Subscribe (printfn "%A")
let sub3 = cntlr.ConfigObs.Subscribe (printfn "%A")

let prevDroneState = ref (enum<ArdroneState>(0))
let showTelemetry = function | DroneState ds when ds = !prevDroneState -> () | DroneState ds -> prevDroneState := ds; printfn "%A" ds | telemetry -> printfn "%A" telemetry
//
let sub2 = cntlr.Telemetry.Subscribe showTelemetry

let connectionCts = new CancellationTokenSource()
Async.Start(cntlr.ConnectAsync(connectionCts))

cntlr.Run (CommonScripts.setConfig {Name="custom:session_id"; Value=cntlr.Session.SessionId })
cntlr.Run (CommonScripts.setConfig {Name="custom:profile_id"; Value=cntlr.Session.UserId})
cntlr.Run (CommonScripts.setConfig {Name="custom:application_id"; Value=cntlr.Session.ApplicationId})

cntlr.Send GetConfig

cntlr.Run (CommonScripts.bootstrap cntlr.Session)


cntlr.Emergency()

(*
stop()
*)
