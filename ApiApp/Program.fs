module ApiApp

open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Falco
open Falco.Routing
open Azure.Messaging.ServiceBus
open Microsoft.AspNetCore.Http
open Shared.ZvrTypes
open Shared.Messaging
open Shared.Topology
open System.Threading.Tasks
open Azure.Storage.Blobs



let builder = WebApplication.CreateBuilder()
let busConnectionString = Environment.GetEnvironmentVariable "SERVICEBUS_CONNECTION_STRING"
let blobConnectionString = Environment.GetEnvironmentVariable "BLOB_CONNECTION_STRING"


let topology = Topology.loadTopology (Path.Combine(AppContext.BaseDirectory, "topology.json"))

let queueName = Topology.getOutgoingQueue "ZvrImportRequest" topology|> Option.defaultValue "error occurred or does not exists"
let containerName = Topology.getArchiveContainer "ZvrImportRequest" topology|> Option.defaultValue "error occurred or does not exists"
let busClient = new ServiceBusClient(busConnectionString)
let blobClient = new BlobServiceClient(blobConnectionString)
let apiKeyHeader = "X-API-KEY"
let validApiKeys = [Environment.GetEnvironmentVariable("X-API-KEY")]

builder.Services.AddSingleton<ServiceBusClient> busClient |> ignore
builder.Services.AddSingleton<BlobServiceClient> blobClient |> ignore

type JobResult =
    { id : string
      status : string }

type BatchResponse =
    { jobs : JobResult list }

let private writeBlob (container: BlobContainerClient) (id: string) (content: string) =
    task {
        let blob = container.GetBlobClient($"{id}.json")
        let bytes = System.Text.Encoding.UTF8.GetBytes content
        use ms = new System.IO.MemoryStream(bytes)
        let! _ =  blob.UploadAsync(ms, overwrite = true)
        return ()
    }


let createBatchHandler
    (decode : string -> Result<'Request, string>)
    (getItems : 'Request -> 'Item list)
    (getId : 'Item -> string)
    (validate : 'Item -> bool)
    (serialize : 'Item -> string)
    (queueName : string)
    (containerName : string)
    : HttpHandler =
    let sender = busClient.CreateSender queueName
    let containerClient = blobClient.GetBlobContainerClient containerName
    fun ctx ->
        task {
            use reader = new StreamReader(ctx.Request.Body)
            let! body = reader.ReadToEndAsync()
            match decode body with
            | Error ex ->
                return!
                    (Response.withStatusCode 400
                     >> Response.ofPlainText ex)
                        ctx
            | Ok request ->
                let! results =
                    request
                    |> getItems
                    |> List.map (fun item ->
                        task {
                            let id = getId item
                            if validate item then
                                let message =
                                    ServiceBusMessage(serialize item)
                                do! sender.SendMessageAsync message
                                do! writeBlob containerClient (DateTime.UtcNow.ToString() + "_" + id) (serialize item)
                                return
                                    { id = id
                                      status = "queued" }
                            else
                                return
                                    { id = id
                                      status = "error" }
                        })
                    |> Task.WhenAll
                return!
                    (Response.withStatusCode 202
                        >>Response.ofJson
                        { jobs = results |> Array.toList })
                        ctx
        }




let validateApiKey (ctx: HttpContext) =
    match ctx.Request.Headers.TryGetValue(apiKeyHeader) with
    | true, keys when List.contains (keys.[0]) validApiKeys -> true
    | _ -> false

let apiKeyMiddleware (next: HttpHandler) (ctx: HttpContext) =
    if validateApiKey ctx then
        next ctx
    else
        let unauthorizedResponse =
            Response.withStatusCode 401 >> Response.ofPlainText "Unauthorized"
        unauthorizedResponse ctx

// POST Handler
let updateZvrDataHandler : HttpHandler =
        createBatchHandler
            ZvrImportRequest.decodeFromString
            (fun r -> r.Zvrs)
            id
            ZvrImportRequest.isValidZvr
            (fun i -> 
                {
                    meta ={
                            messageId = Guid.NewGuid().ToString()
                            correlationId= Guid.NewGuid().ToString()
                            causationId = None
                            createdAt = DateTime.UtcNow 
                            schemaVersion =1}
                    message = UdpdateZvrCommand {zvr = i}
                }|>Envelope.encodeToString)
            queueName
            containerName

let wapp = builder.Build()

wapp.UseRouting()
    .UseFalco([
        get "/" (Response.ofPlainText "Hello World!")
        post "/api/update-zvr" (apiKeyMiddleware updateZvrDataHandler)
    ])
    .Run(Response.withStatusCode 404 >> Response.ofPlainText "Not found")
