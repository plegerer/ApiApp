open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Falco
open Falco.Routing
open Azure.Messaging.ServiceBus

// POST Handler
let postHandler : HttpHandler =
    fun ctx ->
        task {
            use reader = new StreamReader(ctx.Request.Body)
            let! body = reader.ReadToEndAsync()

            let sender = ctx.RequestServices.GetService<ServiceBusSender>()

            let message = ServiceBusMessage(body)
            let! _ =  sender.SendMessageAsync(message)

            return! Response.ofPlainText "Message sent" ctx
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
        get "/" (Response.ofPlainText "Hello World!")
        post "/api/messages" postHandler
    ])
    .Run(Response.ofPlainText "Not found")
