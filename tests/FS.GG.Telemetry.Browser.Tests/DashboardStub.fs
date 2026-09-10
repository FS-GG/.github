namespace FS.GG.Telemetry.Dashboard

type Asset={ContentType:string;Bytes:byte array}
module DashboardAssets =
    let tryGet route =
        match route with
        | "/private/dashboard/" -> Some {ContentType="text/html; charset=utf-8";Bytes=System.Text.Encoding.UTF8.GetBytes "<!doctype html><title>Telemetry</title>"}
        | "/private/dashboard/app.js" -> Some {ContentType="text/javascript; charset=utf-8";Bytes=System.Text.Encoding.UTF8.GetBytes "'use strict';"}
        | "/private/dashboard/styles.css" -> Some {ContentType="text/css; charset=utf-8";Bytes=System.Text.Encoding.UTF8.GetBytes "body{}"}
        | _ -> None
