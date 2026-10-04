module PathControls

open System
open System.Reflection
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
