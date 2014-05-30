namespace FsDrone
open Extensions
open System

type DroneConnection = 
    abstract Cmds:Agent<Command>
    abstract Emergency:Agent<Command>
    abstract Telemtery:IObservable<Telemetery>