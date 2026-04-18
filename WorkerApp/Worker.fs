namespace ServiceBusBlobWorker

open System
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Azure.Messaging.ServiceBus
open Azure.Storage.Blobs
open Microsoft.Extensions.Configuration

type Worker
    (
        logger: ILogger<Worker>,
        serviceBusClient: ServiceBusClient,
        blobServiceClient: BlobServiceClient,
        configuration: IConfiguration
    ) =
    inherit BackgroundService()

    let queueName = configuration.["SERVICEBUS_QUEUE_NAME"]
    let containerName = configuration.["BLOB_CONTAINER_NAME"]

    let processor =
        serviceBusClient.CreateProcessor(queueName, ServiceBusProcessorOptions())

    override _.ExecuteAsync(stoppingToken: CancellationToken) =
        task {
            logger.LogInformation("Worker started")

            let containerClient =
                blobServiceClient.GetBlobContainerClient(containerName)

            let! _ =  containerClient.CreateIfNotExistsAsync()

            // --- Message Handler ---
            let handleMessage (args: ProcessMessageEventArgs) =
                task {
                    try
                        let messageBody = args.Message.Body.ToString()

                        let fileName = $"{Guid.NewGuid()}.txt"
                        let blobClient = containerClient.GetBlobClient(fileName)

                        let bytes = Encoding.UTF8.GetBytes(messageBody)
                        use stream = new IO.MemoryStream(bytes)

                        let! _ = blobClient.UploadAsync(stream)

                        logger.LogInformation("Message saved to blob: {file}", fileName)

                        do! args.CompleteMessageAsync(args.Message)
                    with ex ->
                        logger.LogError(ex, "Error processing message")
                        do! args.AbandonMessageAsync(args.Message)
                } :> Task

            // --- Error Handler ---
            let handleError (args: ProcessErrorEventArgs) =
                logger.LogError(args.Exception, "ServiceBus error")
                Task.CompletedTask

            // --- Zuweisung (der einzige „C#-Teil“) ---
            processor.add_ProcessMessageAsync(Func<_, _>(handleMessage))
            processor.add_ProcessErrorAsync(Func<_, _>(handleError))

            do! processor.StartProcessingAsync(stoppingToken)

            logger.LogInformation("Listening for messages...")

            try
                while not stoppingToken.IsCancellationRequested do
                    do! Task.Delay(1000, stoppingToken)
            finally
                processor.StopProcessingAsync().GetAwaiter().GetResult()
        }