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

System.Console.WriteLine("Enter to get config and bootstrap") 
System.Console.ReadLine() |> ignore

//cntlr.Send GetConfig
//
//cntlr.Run (CommonScripts.bootstrap cntlr.Session)
cntlr.Run (CommonScripts.setConfig {Name="custom:session_id"; Value=cntlr.Session.SessionId })
cntlr.Run (CommonScripts.setConfig {Name="custom:profile_id"; Value=cntlr.Session.UserId})
cntlr.Run (CommonScripts.setConfig {Name="custom:application_id"; Value=cntlr.Session.ApplicationId})

System.Console.WriteLine("enter to close")
System.Console.ReadLine() |> ignore
stop()


(*
cntlr.Emergency()
let isLanded = function Landed      -> true | _ -> false
let isFlying = function Flying _    -> true | _ -> false
System.Console.ReadLine() |> ignore

cntlr.Run (WhenRepeat (isLanded, Takeoff, isFlying))
*)