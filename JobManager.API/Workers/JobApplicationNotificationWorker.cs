
using Amazon;
using Amazon.SQS;
using Amazon.SQS.Model;
using JobManager.API.Configuration;

namespace JobManager.API.Workers
{
    public class JobApplicationNotificationWorker : BackgroundService
    {
        private readonly IConfiguration _configuration;
        private readonly SqsSettings _sqsSettings;
        public JobApplicationNotificationWorker(IConfiguration configuration, SqsSettings sqsSettings)
        {
            _configuration = configuration;
            _sqsSettings = sqsSettings;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var client = new AmazonSQSClient(RegionEndpoint.USEast2);
            var queueUrl = _sqsSettings.QueueUrl;

            while(!stoppingToken.IsCancellationRequested)
            {
                var request = new ReceiveMessageRequest
                {
                    QueueUrl = queueUrl,
                    MessageAttributeNames = ["All"],
                    WaitTimeSeconds = 20,
                    MaxNumberOfMessages = 10
                };

                var response = await client.ReceiveMessageAsync(request, stoppingToken);

                if (response.HttpStatusCode == System.Net.HttpStatusCode.OK)
                {
                    if(response.Messages is null)
                    {
                        Console.WriteLine("No messages received.");
                        continue;
                    }

                    foreach (var message in response.Messages)
                    {
                        // Process the message
                        var body = message.Body;
                        var attributes = message.MessageAttributes;
                        // Log or handle the message as needed
                        Console.WriteLine($"Received message: {body}");
                        // Delete the message after processing
                        var deleteRequest = new DeleteMessageRequest
                        {
                            QueueUrl = queueUrl,
                            ReceiptHandle = message.ReceiptHandle
                        };
                        await client.DeleteMessageAsync(deleteRequest, stoppingToken);
                    }
                }
                else
                {
                    Console.WriteLine($"Failed to receive messages: {response.HttpStatusCode}");
                }
            }
        }
    }
}
