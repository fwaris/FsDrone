//implements the state machines for connection management and sending commands
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
    let startHoverLoop fConnection fDroneState fMonitor =
        async  {
            try
                while true do
                    do! Async.Sleep 30000
                    match fConnection(),fDroneState() with
                    | Disconnected,_ | Connecting _ ,_ -> ()
                    | Connected conn, Flying _ ->
                        let ts = conn.LastCommandSent()
                        let elapsed = (DateTime.Now - ts).TotalSeconds
                        if elapsed > 1.0 then conn.Cmds.Post(Hover)
                    | Connected _, _ -> ()
            with ex ->
                fMonitor (HoverLoopError ex)
        }
    
    //
    let scriptAgent fConnection fDroneState telemetryObs configObs fMonitor (inbox:Agent<Script>) =
        async {
            while true do
                let! script = inbox.Receive()
                try
                    match fConnection() with
                    | Connected conn -> 
                        do! ScriptServices.executeScript 
                                                        fDroneState 
                                                        conn.Cmds.Post
                                                        telemetryObs 
                                                        configObs
                                                        fMonitor 
                                                        script
                    | _ -> ()
                with ex ->
                    fMonitor (ScriptError (script.Name,"no connection"))
        }

type DroneController() = 

    let cts = new System.Threading.CancellationTokenSource()

    let mutable connection = Disconnected
    let setConnection conn = connection <- Connected conn

    let mutable droneState = DroneNavState.Default

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
    let getDroneState() = droneState
                
    let disconnect() =
        match connection with
        | Connected c       -> (c :> IDisposable).Dispose()
        | Connecting cts    -> cts.Cancel()
        | Disconnected      -> ()
        connection <- Disconnected
        fMonitor (ConnectionState ConnectionState.Disconnected)

    do Async.Start(ControllerServices.startHoverLoop getConnection getDroneState fMonitor,cts.Token)

    let fScriptRunner = ControllerServices.scriptAgent  getConnection getDroneState telemtryObservable configObservable fMonitor
    let scriptAgent = Agent.Start(fScriptRunner,cts.Token)

    member x.ConnectAsync (cts:CancellationTokenSource) = connectAsync cts
    member x.Disconnect()   = disconnect()
    member x.Emergency()    = match connection with Connected c -> c.Cmds.Post Emergency | _ -> ()
    member x.Monitor        = monitorObservable
    member x.Telemetry      = telemtryObservable
    member x.ConfigObs      = configObservable
    member x.Run script     = scriptAgent.Post script

    interface IDisposable with
        member x.Dispose() =
            disconnect()
            cts.Cancel()
 
