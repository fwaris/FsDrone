module Extensions
open System
open System.IO
open System.Text
open System.Net
open System.Net.Sockets
open System.Collections.Generic

type Agent<'a> = MailboxProcessor<'a>
type RC<'a>    = AsyncReplyChannel<'a>

let log (s:string)  = System.Console.WriteLine s
let logEx (ex:Exception) = log (sprintf "%s\r%s" ex.Message ex.StackTrace)

type Socket with
    member x.AsyncReceiveFrom (buffer:byte array, endpoint:EndPoint ref) =
        let fBegin = fun (ba,ep,cb,st) -> x.BeginReceiveFrom(ba,0,ba.Length,SocketFlags.None,ep,cb,st)
        let fEnd   = fun ar -> x.EndReceiveFrom(ar,endpoint)
        Async.FromBeginEnd(buffer, endpoint, fBegin, fEnd)

type System.IO.Stream with
    member x.Skip n = x.Position <- x.Position + n