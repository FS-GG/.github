module PathControls

open System
open System.Reflection
open System.IO
open System.Runtime.InteropServices
open FSGG.Telemetry.PersistentV3.ImageClosure

// Invoke the actual private constructor helpers without filesystem or process effects.
let private invoke name value =
    let container=typeof<Selection>.Assembly.GetType("FSGG.Telemetry.PersistentV3.ImageClosure.ImageClosure",true)
    let methodInfo=container.GetMethod(name,BindingFlags.Static ||| BindingFlags.NonPublic)
    if isNull methodInfo then failwith ("constructor helper missing: "+name)
    methodInfo.Invoke(null,[|box value|]) :?> bool

let run () =
    let absolute=invoke "canonicalAbsolute"
    let relative=invoke "relativePath"
    // Actual retained 29 OS data target names, preserving Unicode and timezone names.
    let actualTargets=[|
        "/etc/ssl/certs/NetLock_Arany_=Class_Gold=_Főtanúsítvány.pem"
        "/usr/share/zoneinfo/Etc/GMT+0"
        "/usr/share/zoneinfo/Etc/GMT+1"
        "/usr/share/zoneinfo/Etc/GMT+10"
        "/usr/share/zoneinfo/Etc/GMT+11"
        "/usr/share/zoneinfo/Etc/GMT+12"
        "/usr/share/zoneinfo/Etc/GMT+2"
        "/usr/share/zoneinfo/Etc/GMT+3"
        "/usr/share/zoneinfo/Etc/GMT+4"
        "/usr/share/zoneinfo/Etc/GMT+5"
        "/usr/share/zoneinfo/Etc/GMT+6"
        "/usr/share/zoneinfo/Etc/GMT+7"
        "/usr/share/zoneinfo/Etc/GMT+8"
        "/usr/share/zoneinfo/Etc/GMT+9"
        "/usr/share/zoneinfo/GMT+0"
        "/usr/share/zoneinfo/right/Etc/GMT+0"
        "/usr/share/zoneinfo/right/Etc/GMT+1"
        "/usr/share/zoneinfo/right/Etc/GMT+10"
        "/usr/share/zoneinfo/right/Etc/GMT+11"
        "/usr/share/zoneinfo/right/Etc/GMT+12"
        "/usr/share/zoneinfo/right/Etc/GMT+2"
        "/usr/share/zoneinfo/right/Etc/GMT+3"
        "/usr/share/zoneinfo/right/Etc/GMT+4"
        "/usr/share/zoneinfo/right/Etc/GMT+5"
        "/usr/share/zoneinfo/right/Etc/GMT+6"
        "/usr/share/zoneinfo/right/Etc/GMT+7"
        "/usr/share/zoneinfo/right/Etc/GMT+8"
        "/usr/share/zoneinfo/right/Etc/GMT+9"
        "/usr/share/zoneinfo/right/GMT+0"
    |]
    for value in actualTargets do
        if not(absolute value && relative(value.Substring(1))) then failwith ("retained path refused: "+value)
    let unsafe=[|
        "/etc/../passwd"
        "/etc/./passwd"
        "/etc//passwd"
        "/etc/passwd/"
        "/etc/passwd\n"
        "/etc/a b"
        "/etc/a\\b"
        "/etc/$(id)"
        "/etc/a;id"
        "/etc/a:"
        "/etc/a\t"
        "/"+String.Join("/",Array.create 65 "x")
        "/etc/"+String('é',128)
        "/"+String('a',4097)
        "/etc/a"+string(char 0)+"b"
    |]
    for value in unsafe do
        if absolute value then failwith "unsafe absolute path accepted"
        if value.StartsWith("/") && relative(value.Substring(1)) then failwith "unsafe relative path accepted"
    if absolute "etc/ssl/certs" || relative "/etc/ssl/certs" then failwith "absolute/relative distinction lost"
    if not(absolute("/etc/"+String('a',255))) || absolute("/etc/"+String('a',256)) then failwith "segment byte bound differs"
    if not(absolute("/etc/"+String('é',127))) || absolute("/etc/"+String('é',128)) then failwith "UTF8 segment bound differs"
    let at64="/"+String.Join("/",Array.create 64 "x")
    let at65="/"+String.Join("/",Array.create 65 "x")
    if not(absolute at64) || absolute at65 then failwith "segment count bound differs"
    let at4096="/"+String.Join("/",Array.create 16 (String('a',255)))
    let over4096=at4096+"/a"
    if not(absolute at4096) || absolute over4096 then failwith "path byte bound differs"
    printfn "PASS pure actual constructor path helpers: retained29, unsafe paths, UTF8/count/total bounds"

[<DllImport("libc",SetLastError=true)>]
extern uint32 private getuid()
[<DllImport("libc",SetLastError=true)>]
extern int private mkfifo(string path,uint32 mode)

// Ordinary local files/descriptors only; the FIFO must be inspected without
// opening it for data, so this also detects a blocking replacement for O_PATH.
let metadata () =
    let root=Directory.CreateTempSubdirectory("p2c4-metadata-").FullName
    try
        let regular=invoke "regularFile"
        let expected=int(getuid())
        let file=Path.Combine(root,"file")
        let empty=Path.Combine(root,"empty")
        let unicode=Path.Combine(root,"Főtanúsítvány.pem")
        File.WriteAllText(file,"retained metadata")
        File.WriteAllBytes(empty,Array.empty<byte>)
        File.WriteAllText(unicode,"UTF-8 native path")
        for path in [root;file;empty;unicode] do
            if ImageClosure.ownerUid path<>expected then failwith "descriptor owner differs from actual UID"
        if not(regular file&&regular empty&&regular unicode)||regular root then failwith "descriptor regular/empty/directory/UTF8 distinction lost"
        let link=Path.Combine(root,"file-link")
        let directoryLink=Path.Combine(root,"directory-link")
        let dangling=Path.Combine(root,"dangling-link")
        File.CreateSymbolicLink(link,file)|>ignore
        Directory.CreateSymbolicLink(directoryLink,root)|>ignore
        File.CreateSymbolicLink(dangling,Path.Combine(root,"absent"))|>ignore
        let fifo=Path.Combine(root,"fifo")
        if mkfifo(fifo,0x180u)<>0 then failwith "metadata FIFO fixture creation failed"
        for path in [link;directoryLink;dangling;fifo;Path.Combine(root,"absent");file+string(char 0)+"suffix";""] do
            if ImageClosure.ownerUid path<> -1||regular path then failwith "metadata followed link or accepted invalid/special path"
        // Replacement is re-read, never cached by path, and no descriptor leaks
        // survive repeated success/refusal observations.
        File.Delete file
        File.CreateSymbolicLink(file,empty)|>ignore
        let descriptors=Directory.GetFiles("/proc/self/fd").Length
        for _ in 1..256 do
            if ImageClosure.ownerUid file<> -1||not(regular empty) then failwith "metadata replacement was cached or descriptor failed"
        if Directory.GetFiles("/proc/self/fd").Length<>descriptors then failwith "metadata success/refusal leaked retained descriptors"
        printfn "PASS actual descriptor metadata: UID, regular/empty/directory, final links, FIFO, missing/NUL paths and repeated replacement refusal"
    finally
        Directory.Delete(root,true)
