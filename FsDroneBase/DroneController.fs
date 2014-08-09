//implements the state machines for connection management and sending commands
//also provides monitoring of drone connection
namespace FsDrone
open Extensions
open System.Threading
open System

type ConnectionState = Disconnected | Connecting of CancellationTokenSource | Connected of DroneConnection

type ControllerMsg = Connect | Disconnect | Send of Command

module private Services = 

    let tryConnectAsync fMonitor =
        let rec loop() =
            async {
                let dc = ref None
                try dc := Some (new DroneConnection(fMonitor)) with ex -> logEx ex
                match !dc with 
                | Some c -> return c
                | None -> 
                    do! Async.Sleep 1000
                    return! loop() }
        loop()


type DroneController() = 

    let cts = new System.Threading.CancellationTokenSource()

    let mutable connection = Disconnected
    let setConnection conn = connection <- Connected conn

    let monitorObservable,fMonitor = Observable.createObservableAgent(cts.Token)

    let connect() = 
        match connection with 
        | Disconnected ->
            let cts = new CancellationTokenSource()
            connection <- Connecting cts
            fMonitor (ConnectionState "Connecting...")
            Async.Start(
                async {
                    let! conn = Services.tryConnectAsync fMonitor
                    do setConnection conn
                    fMonitor (ConnectionState "Connected")
                }
                ,cts.Token)
        | _ -> ()
                
    let disconnect() =
        match connection with
        | Connected c       -> (c :> IDisposable).Dispose()
        | Connecting cts    -> cts.Cancel()
        | Disconnected      -> ()
        connection <- Disconnected
        fMonitor (ConnectionState "Disconnected")

    let controllerHandler (inbox:Agent<ControllerMsg>) =
       async {
            while true do
                try
                    let! msg = inbox.Receive()
                    match msg with
                    | Connect -> connect()
                    | Disconnect -> disconnect()
                    | Send cmd -> ()

                with ex -> logEx ex}

    let controllerAgent = Agent.Start(controllerHandler,cts.Token)
 
