module IOUtils
open System.IO
open System.Text

type ReadBuffer(sz) =
    let buffer = Array.create sz 0uy
    let ms = new MemoryStream(buffer)
    let br = new BinaryReader(ms)
    member x.Reset() = ms.Position <- 0L
    member x.Reader = br
    member x.Length = int ms.Position
    member x.ByteArray = buffer
    member x.MaxSize = sz

type WriteBuffer(sz) =
    let buffer = Array.create sz 0uy
    let ms = new MemoryStream(buffer)
    let strw = new StreamWriter(ms,Encoding.ASCII)
    member x.Reset() = ms.Position <- 0L
    member x.TextWriter = strw
    member x.Length = int ms.Position
    member x.ByteArray = buffer
    
