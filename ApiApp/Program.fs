open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Falco
open Falco.Routing
open Azure.Messaging.ServiceBus
open Microsoft.AspNetCore.Http

let apiKeyHeader = "X-API-KEY"
let validApiKeys = [Environment.GetEnvironmentVariable("X-API-KEY")]

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
    fun ctx ->
        task {
            use reader = new StreamReader(ctx.Request.Body)
            let! body = reader.ReadToEndAsync()

            let sender = ctx.RequestServices.GetService<ServiceBusSender>()

            let message = ServiceBusMessage(body)
            try
                let! _ =  sender.SendMessageAsync(message)
                return! (Response.withStatusCode 202 >> Response.ofPlainText "Message sent") ctx
            with ex ->
                return! (Response.withStatusCode 500 >> Response.ofPlainText ex.Message) ctx
        }

let builder = WebApplication.CreateBuilder()
let connectionString = Environment.GetEnvironmentVariable("SERVICEBUS_CONNECTION_STRING")
let queueName = Environment.GetEnvironmentVariable("SERVICEBUS_QUEUE_NAME")
let client = new ServiceBusClient(connectionString)
let sender = client.CreateSender(queueName)

builder.Services.AddSingleton<ServiceBusSender>(sender) |> ignore

let wapp = builder.Build()

wapp.UseRouting()
    .UseFalco([
        //get "/" (Response.ofPlainText "Hello World!")
        post "/api/messages" (apiKeyMiddleware postHandler)
    ])
    .Run(Response.withStatusCode 404 >> Response.ofPlainText "Not found")
