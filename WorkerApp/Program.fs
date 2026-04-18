open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Azure.Messaging.ServiceBus
open Azure.Storage.Blobs

Host.CreateDefaultBuilder()
    .ConfigureServices(fun context services ->

        let sbConnection =
            context.Configuration.["SERVICEBUS_CONNECTION_STRING"]

        let blobConnection =
            context.Configuration.["BLOB_CONNECTION_STRING"]

        services.AddSingleton(ServiceBusClient(sbConnection)) |> ignore
        services.AddSingleton(BlobServiceClient(blobConnection)) |> ignore

        services.AddHostedService<ServiceBusBlobWorker.Worker>() |> ignore
    )
    .Build()
    .Run()