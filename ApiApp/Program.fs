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
open System.Threading.Tasks

let builder = WebApplication.CreateBuilder()
let connectionString = Environment.GetEnvironmentVariable("SERVICEBUS_CONNECTION_STRING")
let queueNameTest = Environment.GetEnvironmentVariable("SERVICEBUS_QUEUE_NAME")
let client = new ServiceBusClient(connectionString)
let apiKeyHeader = "X-API-KEY"
let validApiKeys = [Environment.GetEnvironmentVariable("X-API-KEY")]

builder.Services.AddSingleton<ServiceBusClient>(client) |> ignore

type JobResult =
    { id : string
      status : string }

type BatchResponse =
    { jobs : JobResult list }

let createBatchHandler
    (decode : string -> Result<'Request, string>)
    (getItems : 'Request -> 'Item list)
    (getId : 'Item -> string)
    (validate : 'Item -> bool)
    (serialize : 'Item -> string)
    (queueName : string)
    : HttpHandler =
    let sender = client.CreateSender(queueName)
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
let postHandler : HttpHandler =
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
                    message = UdpdateZrvCommand {zvr = i}
                }|>Envelope.encodeToString)
            queueNameTest

let wapp = builder.Build()

wapp.UseRouting()
    .UseFalco([
        get "/" (Response.ofPlainText "Hello World!")
        post "/api/messages" (apiKeyMiddleware postHandler)
    ])
    .Run(Response.withStatusCode 404 >> Response.ofPlainText "Not found")
