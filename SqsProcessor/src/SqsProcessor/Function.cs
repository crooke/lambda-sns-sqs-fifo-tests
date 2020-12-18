using System.Linq;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System.Collections.Generic;
using System;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SqsProcessor
{
    public class Function
    {
        private readonly AmazonDynamoDBClient amazonDynamoDB;

        /// <summary>
        /// Default constructor. This constructor is used by Lambda to construct the instance. When invoked in a Lambda environment
        /// the AWS credentials will come from the IAM role associated with the function and the AWS region will be set to the
        /// region the Lambda function is executed in.
        /// </summary>
        public Function()
        {
            amazonDynamoDB = new AmazonDynamoDBClient();
        }

        /// <summary>
        /// This method is called for every Lambda invocation. This method takes in an SQS event object and can be used 
        /// to respond to SQS messages.
        /// </summary>
        /// <param name="evnt"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task FunctionHandler(SQSEvent evnt, ILambdaContext context)
        {
            var tasks = evnt.Records.Select(record => ProcessMessageAsync(record, context));
            await Task.WhenAll(tasks);
        }

        private async Task ProcessMessageAsync(SQSEvent.SQSMessage message, ILambdaContext context)
        {
            context.Logger.LogLine($"Processed message {message.Body}");

            var messageBody = System.Text.Json.JsonSerializer.Deserialize<TestMessage>(message.Body);

            await amazonDynamoDB.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = "message-processor",
                Key = new Dictionary<string, AttributeValue>
                {
                    { "pk", new AttributeValue($"SqsProcessor|{messageBody.TestId}") }
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    { ":incr", new AttributeValue { N = "1" }},
                    { ":end", new AttributeValue(DateTimeOffset.UtcNow.ToString("o"))}
                },
                UpdateExpression = "SET MessageCount = MessageCount + :incr, EndTime = :end"
            });
        }

        private class SnsMessageBody
        {
            public TestMessage Message { get; set; }
        }

        private class TestMessage
        {
            public string TestId { get; set; }
            public System.DateTimeOffset StartTime { get; set; }
        }
    }
}
