using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.Lambda.SNSEvents;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System.Collections.Generic;
using System;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SnsProcessor
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
        /// This method is called for every Lambda invocation. This method takes in an SNS event object and can be used 
        /// to respond to SNS messages.
        /// </summary>
        /// <param name="evnt"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task FunctionHandler(SNSEvent evnt, ILambdaContext context)
        {
            // SNS only ever sends one record per event (https://stackoverflow.com/a/47796870)
            await ProcessRecordAsync(evnt.Records[0], context);
        }

        private async Task ProcessRecordAsync(SNSEvent.SNSRecord record, ILambdaContext context)
        {
            context.Logger.LogLine($"Processed record {record.Sns.Message}");

            var message = System.Text.Json.JsonSerializer.Deserialize<TestMessage>(record.Sns.Message);

            await amazonDynamoDB.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = "message-processor",
                Key = new Dictionary<string, AttributeValue>
                {
                    { "pk", new AttributeValue($"SnsProcessor|{message.TestId}") }
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    { ":incr", new AttributeValue { N = "1" }},
                    { ":end", new AttributeValue(DateTimeOffset.UtcNow.ToString("o"))}
                },
                UpdateExpression = "SET Count = Count + :incr, SET EndTime = :end"
            });
        }

        private class TestMessage
        {
            public string TestId { get; set; }
            public DateTimeOffset StartTime { get; set; }
        }
    }
}
