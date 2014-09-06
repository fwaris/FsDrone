//implements the state machines for connection management,
//sending commands and executing scripts
//also provides monitoring of drone connection
namespace FsDrone
open Extensions
open System.Threading
open System
open FsDrone

type private ControllerConnectionState = 
    | Disconnected 
    | Connecting of CancellationTokenSource 
    | Connected of DroneConnection

module private ControllerServices = 

    let applicationId = "fsdrone01"
    let sessionId     = "fsdrone02"
    let userId        = "fsdrone03"

    let tryConnectAsync fMonitor fTelemetry fConfig =
        let rec loop() =
            async {
                let dc = ref None
                try dc := Some (new DroneConnection(fMonitor,fTelemetry, fConfig)) with ex -> logEx ex
                match !dc with 
                | Some c -> return c
                | None -> 
                    do! Async.Sleep 1000
                    return! loop() }
        loop()

    ///sends the Hover command if drone is flying and no commnad has recently been sent
    let startHoverLoop fConnection fPost fMonitor =
        async  {
            try
                while true do
                    do! Async.Sleep 30000
                    match fConnection() with
                    | Disconnected | Connecting _  -> ()
                    | Connected conn ->
                        let ts = conn.LastCommandSent()
                        let elapsed = (DateTime.Now - ts).TotalSeconds
                        if elapsed > 1.0 then fPost CommonScripts.hover
            with ex ->
                fMonitor (HoverLoopError ex)
        }
    
    //
    let scriptAgent fConnection telemetryObs configObs fMonitor (inbox:Agent<Script>) =
        async {
            while true do
                let! script = inbox.Receive()
                printfn "executing %s" script.Name
                try
                    match fConnection() with
                    | Connected conn -> 
                        let! r = ScriptServices.executeScript  
                                                        conn.Cmds.Post
                                                        telemetryObs 
                                                        configObs
                                                        script.Commands
                        match r with
                        | Abort -> fMonitor (ScriptError (script.Name,"aborted"))
                        | _ -> ()
                    | _ -> ()
                with ex ->
                    fMonitor (ScriptError (script.Name,"no connection"))
        }

type DroneController() = 

    let cts = new System.Threading.CancellationTokenSource()

    let mutable connection = Disconnected

    let monitorObservable , fMonitor    = Observable.createObservableAgent(cts.Token)
    let telemtryObservable, fTelemetry  = Observable.createObservableAgent(cts.Token)
    let configObservable  , fConfig     = Observable.createObservableAgent(cts.Token)

    let connectAsync (cts:CancellationTokenSource) = 
        async {
            if cts.IsCancellationRequested then failwith "Controller is disposed - please re-instantiate"
            match connection with 
            | Disconnected ->
                connection <- Connecting cts
                let! conn = ControllerServices.tryConnectAsync fMonitor fTelemetry fConfig
                connection <- Connected conn
                fMonitor (ConnectionState (ConnectionState.Connected conn.Cmds ))
            | Connecting  _ -> return failwith "Connection in progress - please wait or disconnect"
            | Connected c -> ()
        }

    let getConnection() = connection
                
    let disconnect() =
        match connection with
        | Connected c       -> (c :> IDisposable).Dispose()
        | Connecting cts    -> cts.Cancel()
        | Disconnected      -> ()
        connection <- Disconnected
        fMonitor (ConnectionState ConnectionState.Disconnected)


    let fScriptRunner = ControllerServices.scriptAgent  getConnection telemtryObservable configObservable fMonitor
    let scriptAgent = Agent.Start(fScriptRunner,cts.Token)
    do Async.Start(ControllerServices.startHoverLoop getConnection scriptAgent.Post fMonitor,cts.Token)

    member x.ConnectAsync (cts:CancellationTokenSource) = connectAsync cts
    member x.Disconnect()   = disconnect()
    member x.Emergency()    = match connection with Connected c -> c.Cmds.Post Emergency | _ -> ()
    member x.Monitor        = monitorObservable
    member x.Telemetry      = telemtryObservable
    member x.ConfigObs      = configObservable
    member x.Run script     = scriptAgent.Post script
    member x.Send command   = scriptAgent.Post {Name="Send"; Commands=Send command}

    interface IDisposable with
        member x.Dispose() =
            disconnect()
            cts.Cancel()
 
