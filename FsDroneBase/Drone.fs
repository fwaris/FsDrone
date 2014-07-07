namespace FsDrone
open Extensions
open System
open System.Net.Sockets
open System.Net
open System.Text
open System.IO
open IOUtils

module Controller =

    let droneHost     = "192.168.1.1"
    let controlPort   = 5559
    let videoPort     = 5555
    let commandPort   = 5556
    let telemetryPort = 5554 //navdata

    open FsDrone.CommandUtils

    type MonitorMsg =
        | CommandPortError      of Exception
        | TelemeteryPortError   of Exception
        | VideoPortError        of Exception
        | ControlPortError      of Exception

    let createSender token (skt:Socket) endpoint (monitor:Agent<MonitorMsg>) =
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
                        let atCommand = toATCommand buffer.TextWriter !seqNum msg
                        skt.SendTo(buffer.ByteArray,buffer.Length, SocketFlags.None, endpoint) |> ignore
                    with ex -> 
                        logEx ex
                        ex |> CommandPortError |> monitor.Post
            }),
            token)

    let startReceiver 
        token (skt:Socket) endpoint fTelemetryProcessor
        (monitor:Agent<MonitorMsg>) (receiver:Agent<Telemetry>) =
        //
        let buffer = ReadBuffer(4096)
        let seqNum = ref 0
        Async.Start (
            async {
                while true do 
                    try
                        buffer.Reset()
                        let! read = skt.AsyncReceiveFrom(buffer.ByteArray,endpoint)
                        seqNum := fTelemetryProcessor buffer !seqNum receiver.Post
                    with ex ->
                        logEx ex
                        ex |> TelemeteryPortError |> monitor.Post},
            token)


type DroneConnection = 
    abstract Cmds:Agent<Command>
    abstract Emergency:Agent<Command>
    abstract Telemtery:IObservable<Telemetry>