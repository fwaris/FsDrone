module IOUtils
open System.IO
open System.Text

//bundles several types for reading network recieved data
type ReadBuffer(sz) =
    let buffer = Array.create sz 0uy
    let ms = new MemoryStream(buffer)
    let br = new BinaryReader(ms)
    let mutable length = 0
    member x.Reset() = ms.Position <- 0L;  length <- 0
    member x.Reader = br
    member x.Length = int ms.Position
    member x.ByteArray = buffer
    member x.MaxSize = sz
    member x.PrepareForRead l = x.Reset(); length <- l

//bundles several types for writing data
type WriteBuffer(sz) =
    let buffer = Array.create sz 0uy
    let ms = new MemoryStream(buffer)
    let strw = new StreamWriter(ms,Encoding.ASCII)
    member x.Reset() = ms.Position <- 0L
    member x.TextWriter = strw
    member x.Length = int ms.Position
    member x.ByteArray = buffer
        
        
    
