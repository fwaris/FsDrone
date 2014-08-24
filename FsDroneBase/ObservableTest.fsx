open System
#load "Extensions.fs"
open Extensions

let cts = new System.Threading.CancellationTokenSource()
let obs,fPost = Observable.createObservableAgent<string>(cts.Token)

let fobs =
    async {
        obs
        |> Observable.map (fun s -> printfn "%s" s;s)
        |> Observable.till (fun s -> s = "c")
    }

Async.Start(fobs)

fPost "a"
fPost "b"
fPost "c"
fPost "d"

cts.Cancel()
