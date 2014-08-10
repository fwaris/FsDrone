//provides basic connectivity to the drone
//including the ability to send commnads and receive telemetry data
namespace FsDrone
open Extensions
open System
open System.Net.Sockets
open System.Net
open System.Text
open System.IO
open IOUtils
open FsDrone

module private ConnectionServices =

    let droneHost     = "192.168.1.1"
    let controlPort   = 5559
    let videoPort     = 5555
    let commandPort   = 5556
    let telemetryPort = 5554 //navdata

    open FsDrone.CommandUtils

    let createSender token (skt:Socket) endpoint fMonitor =
        let seqNum = ref 0
        let buffer = new WriteBuffer(1000)
        Agent.Start(
            (fun inbox -> 
            async {
                while true do
                    try
                        let! msg = inbox.Receive()
                        seqNum := !seqNum + 1
                        buffer.Reset()
                        toATCommand buffer.TextWriter !seqNum msg
                        skt.SendTo(buffer.ByteArray,buffer.Length, SocketFlags.None, endpoint) |> ignore
                    with ex -> 
                        logEx ex
                        ex |> CommandPortError |> fMonitor
            }),
            token)

    let startReceiver token (skt:Socket) fTelemetryProcessor fError =
        //
        let buffer = ReadBuffer(4096)
        let seqNum = ref 0u
        Async.Start (
            async {
                while true do 
                    try
                        buffer.Reset()
                        let! read = skt.AsycReceive(buffer.ByteArray)
                        buffer.PrepareForRead read
                        seqNum := fTelemetryProcessor fError buffer !seqNum 
                    with ex ->
                        logEx ex
                        fError (UnhandledException ex) },
            token)
        Async.Start (
            async {
                while true do
                    do! Async.Sleep 200
                    skt.Send([|0uy|]) |> ignore
            }, token)


type DroneConnection(fMonitor) = 
    let droneAddr = IPAddress.Parse(ConnectionServices.droneHost)
    let rcvEndpt:EndPoint ref = ref (IPEndPoint(droneAddr,ConnectionServices.telemetryPort) :> EndPoint)
    let sndEndpt = IPEndPoint(droneAddr,ConnectionServices.commandPort)
    let cfgEndpt = IPEndPoint(droneAddr,ConnectionServices.controlPort)

    let sndSocket = new Socket(AddressFamily.InterNetwork,SocketType.Dgram, ProtocolType.Udp)
    let rcvSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram,ProtocolType.Udp)
    //let cfgSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream,ProtocolType.Tcp)

    let connect() =
        try
            sndSocket.Bind(IPEndPoint(IPAddress.Any,ConnectionServices.commandPort))
            sndSocket.Connect(sndEndpt)
            sndSocket.Send([|1uy|]) |> ignore
            rcvSocket.Bind(IPEndPoint(IPAddress.Any,ConnectionServices.telemetryPort))
            rcvSocket.Connect(!rcvEndpt)
            sndSocket.Send([|1uy|]) |> ignore
           // cfgSocket.Connect(cfgEndpt)
        with ex ->
            logEx ex
            sndSocket.Dispose()
            rcvSocket.Dispose()
            fMonitor(ConnectionError ex)
            raise ex

    do connect()

    let cts = new System.Threading.CancellationTokenSource()
    let telemetryObserver,fPost = Observable.createObservableAgent<Telemetry> cts.Token
    let fTelemetry = Parsing.processTelemeteryData fPost
    let sender   = ConnectionServices.createSender cts.Token sndSocket sndEndpt fMonitor
    let fRecvError = fMonitor<<TelemeteryPortError 
    let receiver = ConnectionServices.startReceiver cts.Token rcvSocket fTelemetry fRecvError

    member x.Telemetry = telemetryObserver
    member x.Cmds = sender

    interface IDisposable with 
        member x.Dispose() = 
            try
                cts.Cancel()
                sndSocket.Close(); sndSocket.Dispose()
                rcvSocket.Close(); rcvSocket.Dispose()
            with ex ->
                logEx ex
